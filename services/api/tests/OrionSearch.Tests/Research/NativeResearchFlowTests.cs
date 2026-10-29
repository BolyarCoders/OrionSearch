using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrionSearch.Application.Archives;
using OrionSearch.Application.Content;
using OrionSearch.Application.Research;
using OrionSearch.Tests.Persistence;

namespace OrionSearch.Tests.Research;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class NativeResearchFlowTests(PostgreSqlFixture database, CaptureStorageFixture storage) : IClassFixture<CaptureStorageFixture>
{
    [Fact]
    public async Task NativeAccountCanQueueResearchAndReadStoredResultsWhileAnotherAccountIsDenied()
    {
        var connection = await database.CreateDatabaseAsync();
        try
        {
            await using (var context = database.CreateContext(connection)) await context.Database.MigrateAsync();
            using var root = new ApiFactory("Development");
            using var factory = root.WithSettings(new()
            {
                ["ConnectionStrings:OrionSearch"] = connection,
                ["NativeIdentity:Enabled"] = "true",
                ["NativeIdentity:RequestsPerMinute"] = "1000",
                ["ResearchWorker:Enabled"] = "false"
            }).WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddSingleton<ICaptureStore>(storage.Store)));
            using var owner = factory.CreateClient();
            await SignInAsync(owner, "research-owner@example.com");
            using var created = await owner.PostAsJsonAsync("/archives", new { title = "Account research flow" });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var archive = (await created.Content.ReadFromJsonAsync<ArchiveDetails>())!.Archive.Id;
            using var submitted = await owner.PostAsJsonAsync($"/archives/{archive}/research-jobs", new { objective = "Inspect source evidence" });
            Assert.Equal(HttpStatusCode.Accepted, submitted.StatusCode);
            var run = (await submitted.Content.ReadFromJsonAsync<ResearchStatus>())!;
            Assert.Equal("queued", run.Status);
            Assert.True(submitted.Headers.CacheControl?.NoStore);
            using var pending = await owner.GetAsync($"/research-jobs/{run.Id}");
            Assert.True(pending.Headers.CacheControl?.NoStore);
            Assert.Equal(TimeSpan.FromSeconds(2), pending.Headers.RetryAfter?.Delta);
            var bytes = "<html><body>Research preserves evidence.</body></html>"u8.ToArray();
            var agent = new FakeResearchAgent(async (request, _) =>
            {
                Assert.Equal(archive, request.ArchiveId);
                Assert.Equal("Inspect source evidence", request.Objective);
                var raw = await storage.UploadAsync(request.ResearchRunId, bytes, "text/html");
                var result = ResearchTestData.Output(request.ResearchRunId);
                return result with { Sources = [result.Sources[0] with { Capture = result.Sources[0].Capture! with { Raw = raw } }] };
            });
            Assert.Equal(0, agent.Calls);
            var builder = WorkerHostTests.Builder(connection);
            builder.Services.AddSingleton<IResearchAgentClient>(agent);
            builder.Services.AddSingleton<ICaptureStore>(storage.Store);
            using var worker = builder.Build();
            await worker.StartAsync();
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                ResearchStatus status;
                do
                {
                    status = (await owner.GetFromJsonAsync<ResearchStatus>($"/research-jobs/{run.Id}", deadline.Token))!;
                    if (status.Status == "completed") break;
                    Assert.DoesNotContain(status.Status, new[] { "failed", "cancelled" });
                    await Task.Delay(50, deadline.Token);
                } while (true);
                Assert.Equal(new RunStatistics(1, 1, 1, 1), status.Statistics);
                var history = (await owner.GetFromJsonAsync<Page<ResearchStatus>>($"/archives/{archive}/research-jobs"))!;
                Assert.Equal(run.Id, Assert.Single(history.Items).Id);
                var findings = (await owner.GetFromJsonAsync<Page<FindingSummary>>($"/archives/{archive}/findings"))!;
                var finding = Assert.Single(findings.Items);
                var detail = (await owner.GetFromJsonAsync<FindingDetails>($"/archives/{archive}/findings/{finding.Id}"))!;
                var evidence = Assert.Single(detail.Evidence.Items);
                var sourcePath = $"/archives/{archive}/sources/{evidence.DocumentId}";
                var source = (await owner.GetFromJsonAsync<SourceDetails>(sourcePath))!;
                Assert.Equal(1, source.VersionCount);
                var versionPath = sourcePath + $"/versions/{evidence.VersionId}";
                var captured = (await owner.GetFromJsonAsync<CapturedVersion>(versionPath))!;
                Assert.Contains(evidence.Quote!, captured.ExtractedText!);
                using var raw = await owner.GetAsync(versionPath + "/raw");
                Assert.Equal(HttpStatusCode.OK, raw.StatusCode);
                Assert.Equal(bytes, await raw.Content.ReadAsByteArrayAsync());

                using var outsider = factory.CreateClient();
                await SignInAsync(outsider, "outsider@example.com");
                foreach (var path in new[] { $"/research-jobs/{run.Id}", $"/archives/{archive}/research-jobs", $"/archives/{archive}/findings",
                    $"/archives/{archive}/findings/{finding.Id}", sourcePath, sourcePath + "/versions", versionPath, versionPath + "/raw" })
                    Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync(path)).StatusCode);
            }
            finally { await worker.StopAsync(); }
            Assert.Equal(1, agent.Calls);
        }
        finally { await database.DropDatabaseAsync(connection); }
    }

    private static async Task SignInAsync(HttpClient client, string email)
    {
        const string password = "Research!Account2026";
        using var registered = await client.PostAsJsonAsync("/auth/register", new { email, password });
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        using var response = await client.PostAsJsonAsync("/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tokens = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokens.GetProperty("accessToken").GetString());
    }
}
