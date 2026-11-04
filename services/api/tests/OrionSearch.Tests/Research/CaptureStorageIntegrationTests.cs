using System.Net;
using System.Net.Http.Json;
using Amazon.S3.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrionSearch.Application.Archives;
using OrionSearch.Application.Content;
using OrionSearch.Application.Research;
using OrionSearch.Domain.Content;
using OrionSearch.Tests.Archives;
using OrionSearch.Tests.Persistence;

namespace OrionSearch.Tests.Research;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class CaptureStorageIntegrationTests(PostgreSqlFixture database, CaptureStorageFixture storage) : IClassFixture<CaptureStorageFixture>
{
    [Fact]
    public async Task RealVersionedBytesSurviveReplacementAndMissingObjectsAreRejected()
    {
        var run = Guid.NewGuid();
        var raw = await storage.UploadAsync(run, "actual bytes"u8.ToArray());
        var uri = new Uri(raw.StorageUri);
        var key = uri.AbsolutePath.TrimStart('/');
        using var replacement = new MemoryStream("replacement"u8.ToArray());
        var changed = await storage.Client.PutObjectAsync(new PutObjectRequest { BucketName = storage.Options.Bucket, Key = key, InputStream = replacement, ContentType = "text/plain" });
        Assert.Equal("actual bytes"u8.ToArray(), (await storage.Store.ReadAsync(raw.StorageUri, raw.Sha256, run, default)).Bytes);
        var corrupted = raw.StorageUri.Split('?')[0] + "?versionId=" + Uri.EscapeDataString(changed.VersionId);
        await Assert.ThrowsAsync<ResearchAgentException>(() => storage.Store.ReadAsync(corrupted, raw.Sha256, run, default));
        var missing = raw.StorageUri.Split('?')[0] + "?versionId=missing-version";
        await Assert.ThrowsAsync<ResearchAgentException>(() => storage.Store.ReadAsync(missing, raw.Sha256, run, default));
        var corrupt = await storage.UploadAsync(run, "wrong bytes"u8.ToArray());
        // A mismatched path/digest cannot be used to bless unrelated bytes.
        await Assert.ThrowsAsync<ResearchAgentException>(() => storage.Store.ReadAsync(corrupt.StorageUri, raw.Sha256, run, default));
    }

    [Fact]
    public async Task AcceptedVersionAndRawBytesAreAuthorizedWithoutLeakingGlobalRepresentations()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database, services => services.AddSingleton<ICaptureStore>(storage.Store));
        var run = await ResearchTestData.SubmitAsync(scenario);
        var data = "<html><body>Research preserves evidence.</body></html>"u8.ToArray();
        var raw = await storage.UploadAsync(run.Id, data, "text/html");
        await using var scope = scenario.Scope();
        var queue = ResearchTestData.Queue(scope);
        var output = ResearchTestData.Output(run.Id);
        output = output with { Sources = [output.Sources[0] with { Capture = output.Sources[0].Capture! with { Raw = raw } }] };
        Assert.True(await queue.CompleteAsync((await queue.ClaimAsync("real-storage", default))!, output, default));
        await using var context = scenario.Context();
        var version = await context.DocumentVersions.SingleAsync();
        var path = $"/archives/{scenario.ArchiveId}/sources/{version.DocumentId}/versions/{version.Id}";
        var listPath = $"/archives/{scenario.ArchiveId}/sources/{version.DocumentId}/versions";
        foreach (var role in new[] { "owner", "editor", "viewer" })
        {
            using var client = scenario.Client(role);
            var versions = (await client.GetFromJsonAsync<Page<CapturedVersionSummary>>(listPath))!;
            Assert.Equal(version.Id, Assert.Single(versions.Items).Id);
            var captured = (await client.GetFromJsonAsync<CapturedVersion>(path))!;
            Assert.Equal(version.ExtractedText, captured.ExtractedText);
            Assert.True(captured.HasRaw);
            using var response = await client.GetAsync(path + "/raw");
            Assert.Equal(data, await response.Content.ReadAsByteArrayAsync());
            Assert.Equal("application/octet-stream", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
            Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
            Assert.True(response.Headers.CacheControl?.NoStore);
            var json = await client.GetStringAsync(path);
            Assert.DoesNotContain("s3://", json);
        }
        using var nonmember = scenario.Client("nonmember");
        Assert.Equal(HttpStatusCode.NotFound, (await nonmember.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await nonmember.GetAsync(path + "/raw")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await nonmember.GetAsync(listPath)).StatusCode);
        using var anonymous = scenario.Client(null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path + "/raw")).StatusCode);
        using var disabled = scenario.Client("disabled");
        Assert.Equal(HttpStatusCode.Unauthorized, (await disabled.GetAsync(path)).StatusCode);

        // A second archive accepts identical extracted text, without this raw
        // representation. Global dedup must not grant the old HTML bytes.
        using var owner = scenario.Client();
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync(listPath + "?pageSize=101")).StatusCode);
        using var created = await owner.PostAsJsonAsync("/archives", new { title = "Second archive" });
        var other = (await created.Content.ReadFromJsonAsync<ArchiveDetails>())!.Archive.Id;
        using var queued = await owner.PostAsJsonAsync($"/archives/{other}/research-jobs", new { });
        var otherRun = (await queued.Content.ReadFromJsonAsync<ResearchStatus>())!;
        Assert.True(await queue.CompleteAsync((await queue.ClaimAsync("text-only", default))!, ResearchTestData.Output(otherRun.Id), default));
        var otherPath = $"/archives/{other}/sources/{version.DocumentId}/versions/{version.Id}";
        Assert.False((await owner.GetFromJsonAsync<CapturedVersion>(otherPath))!.HasRaw);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(otherPath + "/raw")).StatusCode);
        Assert.Equal(2, await context.ArchiveDocumentVersions.CountAsync());

        // A newly discovered global document alone does not grant any version.
        await context.ArchiveDocumentVersions.Where(grant => grant.ArchiveId == other).ExecuteDeleteAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(otherPath)).StatusCode);
        Assert.Empty((await owner.GetFromJsonAsync<Page<CapturedVersionSummary>>($"/archives/{other}/sources/{version.DocumentId}/versions"))!.Items);

        // Only test-admin credentials can delete an immutable version. API reads
        // refuse to serve replacement/unverified bytes if storage is damaged.
        var rawUri = new Uri(raw.StorageUri);
        await storage.Client.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = storage.Options.Bucket,
            Key = rawUri.AbsolutePath.TrimStart('/'),
            VersionId = Uri.UnescapeDataString(rawUri.Query["?versionId=".Length..])
        });
        using var unavailable = await owner.GetAsync(path + "/raw");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.DoesNotContain("s3://", await unavailable.Content.ReadAsStringAsync());
    }
}
