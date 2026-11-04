using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OrionSearch.Application.Archives;
using OrionSearch.Application.Content;
using OrionSearch.Application.Research;
using OrionSearch.Infrastructure.Persistence;
using OrionSearch.Tests.Research;
using Testcontainers.PostgreSql;

namespace OrionSearch.Tests.Persistence;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class RestoreAcceptanceTests(CaptureStorageFixture storage) : IClassFixture<CaptureStorageFixture>
{
    [Fact]
    public async Task IsolatedDumpAndObjectRestorePreservesDataRevokesSessionsAndFencesOldWorkers()
    {
        var image = Environment.GetEnvironmentVariable("ORIONSEARCH_TEST_POSTGRES_IMAGE") ?? "postgres:16-alpine";
        await using var postgres = new PostgreSqlBuilder(image).WithDatabase("source").WithPassword(Guid.NewGuid().ToString("N")).Build();
        await postgres.StartAsync();
        var sourceConnection = postgres.GetConnectionString();
        var targetConnection = new NpgsqlConnectionStringBuilder(sourceConnection) { Database = "restored" }.ConnectionString;
        OrionSearchDbContext Context(string connection) => new(new DbContextOptionsBuilder<OrionSearchDbContext>()
            .UseNpgsql(connection, options => options.SetPostgresVersion(16, 0).MigrationsHistoryTable("__ef_migrations_history", "system")).Options);
        using var rsa = RSA.Create(2048);
        var certificateRequest = new CertificateRequest("CN=OrionSearch restore acceptance", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        using var root = new ApiFactory("Development");
        WebApplicationFactory<Program> Factory(string connection) => root.WithSettings(new()
        {
            ["ConnectionStrings:OrionSearch"] = connection,
            ["NativeIdentity:Enabled"] = "true",
            ["NativeIdentity:RequestsPerMinute"] = "1000",
            ["ResearchWorker:Enabled"] = "false"
        }).WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton<ICaptureStore>(storage.Store);
            services.AddDataProtection().ProtectKeysWithCertificate(certificate);
        }));
        await using (var source = Context(sourceConnection)) await source.Database.MigrateAsync();
        using var sourceFactory = Factory(sourceConnection);
        using var owner = sourceFactory.CreateClient();
        const string email = "restore-owner@example.com";
        const string password = "Research!Restore2026";
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync("/auth/register", new { email, password })).StatusCode);
        using var login = await owner.PostAsJsonAsync("/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var tokens = await login.Content.ReadFromJsonAsync<JsonElement>();
        owner.DefaultRequestHeaders.Authorization = new("Bearer", tokens.GetProperty("accessToken").GetString());
        var profile = await owner.GetFromJsonAsync<JsonElement>("/me");
        var userId = profile.GetProperty("id").GetGuid();
        using var created = await owner.PostAsJsonAsync("/archives", new { title = "Restore acceptance archive" });
        var archive = (await created.Content.ReadFromJsonAsync<ArchiveDetails>())!.Archive.Id;
        using var submitted = await owner.PostAsJsonAsync($"/archives/{archive}/research-jobs", new { });
        var run = (await submitted.Content.ReadFromJsonAsync<ResearchStatus>())!;
        var rawBytes = "<html><body>Research preserves evidence.</body></html>"u8.ToArray();
        var raw = await storage.UploadAsync(run.Id, rawBytes, "text/html");
        var output = ResearchTestData.Output(run.Id);
        output = output with { Sources = [output.Sources[0] with { Capture = output.Sources[0].Capture! with { Raw = raw } }] };
        ResearchLease oldLease;
        using (var scope = sourceFactory.Services.CreateScope())
        {
            var queue = scope.ServiceProvider.GetRequiredService<IResearchQueue>();
            Assert.True(await queue.CompleteAsync((await queue.ClaimAsync("source-completion", default))!, output, default));
            using var pending = await owner.PostAsJsonAsync($"/archives/{archive}/research-jobs", new { objective = "Interrupted work" });
            Assert.Equal(HttpStatusCode.Accepted, pending.StatusCode);
            oldLease = (await queue.ClaimAsync("source-interrupted-worker", default))!;
        }
        await using (var source = Context(sourceConnection))
        {
            Assert.All(await source.DataProtectionKeys.ToListAsync(), key => Assert.Contains("encryptedSecret", key.Xml));
            Assert.Equal(2, await source.ResearchRuns.CountAsync());
        }

        // Drain application writers before taking the snapshot. Only synthetic,
        // fixture-owned data is exported; no production connection is accepted.
        var protectedProbe = sourceFactory.Services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("restore-acceptance").Protect("restored key material");
        sourceFactory.Dispose();
        var dump = await postgres.ExecAsync(["pg_dump", "--username=postgres", "--dbname=source", "--format=custom", "--no-owner", "--no-acl", "--file=/tmp/source.dump"]);
        Assert.Equal(0, dump.ExitCode);
        var backup = await postgres.ReadFileAsync("/tmp/source.dump");
        Assert.True(backup.Length > 0);
        var objectBackup = (await storage.Store.ReadAsync(raw.StorageUri, raw.Sha256, run.Id, default)).Bytes;
        Assert.Equal(rawBytes, objectBackup);
        Assert.Equal(0, (await postgres.ExecAsync(["createdb", "--username=postgres", "restored"])).ExitCode);
        await using (var empty = Context(targetConnection)) Assert.Empty(await empty.Database.GetAppliedMigrationsAsync());
        await postgres.CopyAsync(backup, "/tmp/restore.dump");
        Assert.Equal(0, (await postgres.ExecAsync(["pg_restore", "--username=postgres", "--dbname=restored", "--exit-on-error", "--no-owner", "--no-acl", "/tmp/restore.dump"])).ExitCode);
        // The pre-restore emptiness probe cached a type catalog without citext.
        // Application consumers start only after restore/DDL and fresh type loading.
        await using (var fresh = new NpgsqlConnection(targetConnection))
        {
            await fresh.OpenAsync();
            await fresh.ReloadTypesAsync();
        }

        // Restoring bytes into a different immutable version needs an explicit,
        // transactional reference mapping. The original objects remain intact.
        var restoredRaw = await storage.UploadAsync(run.Id, objectBackup, "text/html");
        await using (var target = Context(targetConnection))
        {
            Assert.Equal(target.Database.GetMigrations(), await target.Database.GetAppliedMigrationsAsync());
            Assert.Equal(userId, (await target.Users.SingleAsync()).Id);
            Assert.Equal(2, await target.ResearchRuns.CountAsync());
            Assert.Single(await target.Findings.ToListAsync());
            Assert.Single(await target.FindingEvidence.ToListAsync());
            await using var transaction = await target.Database.BeginTransactionAsync();
            var representationPath = new[] { "representations", "raw", "storage_uri" };
            await target.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE content.document_versions SET storage_uri = {restoredRaw.StorageUri},
                    metadata = jsonb_set(metadata, {representationPath}, to_jsonb({restoredRaw.StorageUri}::text))
                WHERE storage_uri = {raw.StorageUri}
                """);
            await target.Database.ExecuteSqlInterpolatedAsync($"UPDATE content.archive_document_versions SET storage_uri = {restoredRaw.StorageUri} WHERE storage_uri = {raw.StorageUri}");
            // A point-in-time restore must not revive old sessions/worker leases.
            await target.Database.ExecuteSqlRawAsync("UPDATE auth.users SET security_stamp = gen_random_uuid()::text, concurrency_stamp = gen_random_uuid()::text");
            await target.Database.ExecuteSqlRawAsync("UPDATE system.jobs SET worker_id = NULL, locked_until = clock_timestamp() - interval '1 second' WHERE status = 'processing'");
            await transaction.CommitAsync();
            Assert.Equal(raw.Sha256, (await target.ArchiveDocumentVersions.SingleAsync()).RawSha256);
            Assert.All(await target.DataProtectionKeys.ToListAsync(), key => Assert.Contains("encryptedSecret", key.Xml));
        }
        using var targetFactory = Factory(targetConnection);
        Assert.Equal("restored key material", targetFactory.Services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("restore-acceptance").Unprotect(protectedProbe));
        using var restored = targetFactory.CreateClient();
        restored.DefaultRequestHeaders.Authorization = owner.DefaultRequestHeaders.Authorization;
        Assert.Equal(HttpStatusCode.Unauthorized, (await restored.GetAsync("/me")).StatusCode);
        using var refreshed = await restored.PostAsJsonAsync("/auth/refresh", new { refreshToken = tokens.GetProperty("refreshToken").GetString() });
        Assert.Equal(HttpStatusCode.Unauthorized, refreshed.StatusCode);
        using var relogin = await restored.PostAsJsonAsync("/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, relogin.StatusCode);
        var freshTokens = await relogin.Content.ReadFromJsonAsync<JsonElement>();
        restored.DefaultRequestHeaders.Authorization = new("Bearer", freshTokens.GetProperty("accessToken").GetString());
        Assert.Equal(userId, (await restored.GetFromJsonAsync<JsonElement>("/me")).GetProperty("id").GetGuid());
        var findings = (await restored.GetFromJsonAsync<Page<FindingSummary>>($"/archives/{archive}/findings"))!;
        var finding = Assert.Single(findings.Items);
        var detail = (await restored.GetFromJsonAsync<FindingDetails>($"/archives/{archive}/findings/{finding.Id}"))!;
        var evidence = Assert.Single(detail.Evidence.Items);
        var versionPath = $"/archives/{archive}/sources/{evidence.DocumentId}/versions/{evidence.VersionId}";
        var capture = (await restored.GetFromJsonAsync<CapturedVersion>(versionPath))!;
        Assert.Contains(evidence.Quote!, capture.ExtractedText!);
        using var download = await restored.GetAsync(versionPath + "/raw");
        Assert.Equal(rawBytes, await download.Content.ReadAsByteArrayAsync());
        using (var scope = targetFactory.Services.CreateScope())
        {
            var queue = scope.ServiceProvider.GetRequiredService<IResearchQueue>();
            Assert.False(await queue.HeartbeatAsync(oldLease, default));
            Assert.False(await queue.CompleteAsync(oldLease, ResearchTestData.Output(oldLease.RunId), default));
            Assert.Equal(1, await queue.RecoverAsync(default));
            await using var context = Context(targetConnection);
            await context.Database.ExecuteSqlRawAsync("UPDATE system.jobs SET run_after = clock_timestamp() - interval '1 second' WHERE status = 'pending'");
            var next = (await queue.ClaimAsync("restored-worker", default))!;
            Assert.Equal(oldLease.Attempt + 1, next.Attempt);
            Assert.True(await queue.CompleteAsync(next, ResearchTestData.Output(next.RunId), default));
        }
        using var outsider = targetFactory.CreateClient();
        Assert.Equal(HttpStatusCode.Created, (await outsider.PostAsJsonAsync("/auth/register", new { email = "restore-outsider@example.com", password })).StatusCode);
        using var otherLogin = await outsider.PostAsJsonAsync("/auth/login", new { email = "restore-outsider@example.com", password });
        var otherTokens = await otherLogin.Content.ReadFromJsonAsync<JsonElement>();
        outsider.DefaultRequestHeaders.Authorization = new("Bearer", otherTokens.GetProperty("accessToken").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync(versionPath + "/raw")).StatusCode);
        Assert.Equal(rawBytes, (await storage.Store.ReadAsync(raw.StorageUri, raw.Sha256, run.Id, default)).Bytes);
        await using var retainedSource = Context(sourceConnection);
        Assert.Equal("running", (await retainedSource.ResearchRuns.SingleAsync(value => value.Id == oldLease.RunId)).Status);
        Assert.Equal(raw.StorageUri, (await retainedSource.ArchiveDocumentVersions.SingleAsync()).StorageUri);
    }
}
