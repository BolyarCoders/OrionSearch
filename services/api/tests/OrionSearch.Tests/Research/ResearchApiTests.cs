using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OrionSearch.Application.Research;
using OrionSearch.Tests.Archives;
using OrionSearch.Tests.Persistence;

namespace OrionSearch.Tests.Research;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class ResearchApiTests(PostgreSqlFixture database)
{
    [Theory]
    [InlineData("owner", 202)]
    [InlineData("editor", 202)]
    [InlineData("viewer", 403)]
    [InlineData("nonmember", 404)]
    [InlineData(null, 401)]
    public async Task SubmissionUsesPersistedRolesAndQueuesWithoutRunningTheAgent(string? role, int status)
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        using var client = scenario.Client(role);
        using var response = await client.PostAsJsonAsync($"/archives/{scenario.ArchiveId}/research-jobs", new { objective = "A valid objective" });
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        await using var context = scenario.Context();
        if (status != 202)
        {
            Assert.Empty(await context.ResearchRuns.ToListAsync());
            Assert.Empty(await context.Jobs.ToListAsync());
            return;
        }
        var result = (await response.Content.ReadFromJsonAsync<ResearchStatus>())!;
        var run = await context.ResearchRuns.SingleAsync();
        var job = await context.Jobs.SingleAsync();
        Assert.Equal(run.Id, result.Id);
        Assert.Equal(run.Id, job.ResearchRunId);
        Assert.NotEqual(job.Id, result.Id);
        Assert.Equal("queued", result.Status);
        Assert.Equal("pending", job.Status);
        Assert.Equal("research_plan", job.JobType);
        Assert.Equal(0, job.Attempts);
        Assert.Null(run.StartedAt);
        Assert.Empty(await context.JobAttempts.ToListAsync());
        Assert.Equal($"/research-jobs/{run.Id}", response.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("owner", 200)]
    [InlineData("editor", 200)]
    [InlineData("viewer", 200)]
    [InlineData("nonmember", 404)]
    [InlineData(null, 401)]
    public async Task StatusRequiresMembershipInTheOriginatingArchive(string? role, int expected)
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        var run = await ResearchTestData.SubmitAsync(scenario);
        using var client = scenario.Client(role);
        using var response = await client.GetAsync($"/research-jobs/{run.Id}");
        Assert.Equal((HttpStatusCode)expected, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("payload", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("workerId", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("objective", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AgentApiKey", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvalidObjectivesSpoofedFieldsAndUnknownRunIdsFailWithoutWrites()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        using var client = scenario.Client();
        foreach (var body in new[] { "{\"objective\":\"\"}", "{\"objective\":\"   \"}", "{\"userId\":\"spoof\"}", "{\"ownerId\":\"spoof\"}", "{\"role\":\"owner\"}", "{\"settings\":{}}" })
        {
            using var response = await client.PostAsync($"/archives/{scenario.ArchiveId}/research-jobs", new StringContent(body, Encoding.UTF8, "application/json"));
            await ArchiveApiTests.AssertProblemAsync(response, HttpStatusCode.BadRequest);
        }
        using var oversized = await client.PostAsJsonAsync($"/archives/{scenario.ArchiveId}/research-jobs", new { objective = new string('x', 2001) });
        await ArchiveApiTests.AssertProblemAsync(oversized, HttpStatusCode.BadRequest);
        using var unknown = await client.GetAsync($"/research-jobs/{Guid.NewGuid()}");
        using var malformed = await client.GetAsync("/research-jobs/not-a-uuid");
        await ArchiveApiTests.AssertProblemAsync(unknown, HttpStatusCode.NotFound);
        await ArchiveApiTests.AssertProblemAsync(malformed, HttpStatusCode.NotFound);
        await using var context = scenario.Context();
        Assert.Empty(await context.ResearchRuns.ToListAsync());
    }

    [Theory]
    [InlineData("paused")]
    [InlineData("archived")]
    public async Task InactiveArchivesRejectNewSubmissions(string status)
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        await using var context = scenario.Context();
        await context.Archives.ExecuteUpdateAsync(setters => setters.SetProperty(archive => archive.Status, status));
        using var client = scenario.Client();
        using var response = await client.PostAsJsonAsync($"/archives/{scenario.ArchiveId}/research-jobs", new { });
        await ArchiveApiTests.AssertProblemAsync(response, HttpStatusCode.Conflict);
        Assert.Empty(await context.ResearchRuns.ToListAsync());
    }

    [Fact]
    public async Task JobInsertFailureRollsBackTheRun()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        await using var context = scenario.Context();
        await context.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION public.reject_job() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'private injected failure'; END $$;
            CREATE TRIGGER reject_job BEFORE INSERT ON system.jobs FOR EACH ROW EXECUTE FUNCTION public.reject_job();
            """);
        using var client = scenario.Client();
        using var response = await client.PostAsJsonAsync($"/archives/{scenario.ArchiveId}/research-jobs", new { });
        await ArchiveApiTests.AssertProblemAsync(response, HttpStatusCode.InternalServerError);
        Assert.DoesNotContain("private injected failure", await response.Content.ReadAsStringAsync());
        Assert.Empty(await context.ResearchRuns.ToListAsync());
        Assert.Empty(await context.Jobs.ToListAsync());
    }

    [Fact]
    public async Task IdempotencySerializesConcurrentDuplicatesAndDetectsDifferentRequests()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => ResearchTestData.SubmitAsync(scenario, "repeat-key")));
        Assert.Single(results.Select(result => result.Id).Distinct());
        using var client = scenario.Client();
        using var incompatible = new HttpRequestMessage(HttpMethod.Post, $"/archives/{scenario.ArchiveId}/research-jobs")
        { Content = JsonContent.Create(new { objective = "Different objective" }) };
        incompatible.Headers.Add("Idempotency-Key", "repeat-key");
        using var conflict = await client.SendAsync(incompatible);
        await ArchiveApiTests.AssertProblemAsync(conflict, HttpStatusCode.Conflict);
        await using var context = scenario.Context();
        Assert.Single(await context.ResearchRuns.ToListAsync());
        Assert.Single(await context.Jobs.ToListAsync());
        var first = await ResearchTestData.SubmitAsync(scenario);
        var second = await ResearchTestData.SubmitAsync(scenario);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public async Task IdempotencyKeysAreArchiveScopedAndDefaultObjectiveIsSnapshotted()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        var first = await ResearchTestData.SubmitAsync(scenario, "same-key", null);
        using var client = scenario.Client();
        using var created = await client.PostAsJsonAsync("/archives", new { title = "Second archive" });
        var archive = (await created.Content.ReadFromJsonAsync<OrionSearch.Application.Archives.ArchiveDetails>())!;
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/archives/{archive.Archive.Id}/research-jobs") { Content = JsonContent.Create(new { }) };
        request.Headers.Add("Idempotency-Key", "same-key");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.NotEqual(first.Id, (await response.Content.ReadFromJsonAsync<ResearchStatus>())!.Id);
        await using var context = scenario.Context();
        var job = await context.Jobs.SingleAsync(job => job.ResearchRunId == first.Id);
        var payload = System.Text.Json.JsonSerializer.Deserialize<ResearchPayload>(job.Payload, ResearchContract.Json)!;
        Assert.Equal("Existing archive", payload.Request.Objective);
        Assert.Equal(new[] { "en" }, payload.Request.Settings.Languages);
        Assert.DoesNotContain("same-key", job.IdempotencyKey);
    }
}
