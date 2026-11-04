using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrionSearch.Application.Research;
using OrionSearch.Application.Archives;
using System.Text;
using OrionSearch.Tests.Archives;
using OrionSearch.Tests.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;

namespace OrionSearch.Tests.Research;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class ResearchLimitsTests(PostgreSqlFixture database)
{
    [Fact]
    public async Task LegacyDailyLimitDoesNotBlockAnotherApiHost()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database, services => services.PostConfigure<ResearchLimitOptions>(limits =>
        {
            limits.MaxRunsPerOwnerPerDay = 1;
            limits.MaxRunsGlobalPerDay = 1;
            limits.MaxRunsPerOwnerPerMinute = 1;
        }), useDefaultResearchLimits: true);
        await ResearchTestData.SubmitAsync(scenario);
        using var root = new ApiFactory();
        using var factory = root.WithSettings(new()
        {
            ["ConnectionStrings:OrionSearch"] = scenario.ConnectionString,
            ["ResearchLimits:MaxRunsPerOwnerPerDay"] = "1",
            ["ResearchLimits:MaxRunsGlobalPerDay"] = "1",
            ["ResearchLimits:MaxRunsPerOwnerPerMinute"] = "1"
        }).WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddAuthentication("ArchiveTest")
            .AddScheme<AuthenticationSchemeOptions, ArchiveTestAuthenticationHandler>("ArchiveTest", _ => { })));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Identity", scenario.Users["owner"].ToString());
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync($"/archives/{scenario.ArchiveId}/research-jobs", new { })).StatusCode);
        await using var context = scenario.Context();
        Assert.Equal(2, await context.Jobs.CountAsync());
    }

    [Fact]
    public async Task OversizedSubmissionBodiesAreRejectedBeforeWorkIsCreated()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database, useDefaultResearchLimits: true);
        using var client = scenario.Client();
        using var body = new StringContent("{\"objective\":\"" + new string('s', 20000) + "\"}", Encoding.UTF8, "application/json");
        using var response = await client.PostAsync($"/archives/{scenario.ArchiveId}/research-jobs", body);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        await using var context = scenario.Context();
        Assert.Empty(await context.Jobs.ToListAsync());
    }

    [Theory]
    [InlineData("owner-outstanding")]
    [InlineData("global-outstanding")]
    public async Task NewArchiveOrCollaboratorCannotBypassOwnerAndGlobalLimits(string kind)
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database, services => services.PostConfigure<ResearchLimitOptions>(limits =>
        {
            switch (kind)
            {
                case "owner-outstanding": limits.MaxOutstandingPerOwner = 1; break;
                default: limits.MaxOutstandingGlobal = 1; break;
            }
        }), useDefaultResearchLimits: true);
        using var editor = scenario.Client("editor");
        Assert.Equal(HttpStatusCode.Accepted, (await editor.PostAsJsonAsync($"/archives/{scenario.ArchiveId}/research-jobs", new { })).StatusCode);
        using var creator = scenario.Client(kind.StartsWith("global", StringComparison.Ordinal) ? "nonmember" : "owner");
        using var created = await creator.PostAsJsonAsync("/archives", new { title = "A different archive" });
        var id = (await created.Content.ReadFromJsonAsync<ArchiveDetails>())!.Archive.Id;
        using var refused = await creator.PostAsJsonAsync($"/archives/{id}/research-jobs", new { });
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.True(refused.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.True(refused.Headers.CacheControl?.NoStore);
        await using var context = scenario.Context();
        Assert.Single(await context.ResearchRuns.ToListAsync());
        Assert.Single(await context.Jobs.ToListAsync());
    }

    [Fact]
    public async Task ConcurrentSubmissionsReserveQueueCapacityAtomically()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database, services => services.PostConfigure<ResearchLimitOptions>(limits =>
        {
            limits.MaxOutstandingPerOwner = 3;
            limits.MaxRunsPerOwnerPerMinute = limits.MaxSubmissionsPerIpPerMinute = 1000;
        }), useDefaultResearchLimits: true);
        using var client = scenario.Client();
        using var created = await client.PostAsJsonAsync("/archives", new { title = "Second archive" });
        var other = (await created.Content.ReadFromJsonAsync<ArchiveDetails>())!.Archive.Id;
        var responses = await Task.WhenAll(Enumerable.Range(0, 12).Select(index => client.PostAsJsonAsync(
            $"/archives/{(index % 2 == 0 ? scenario.ArchiveId : other)}/research-jobs", new { objective = $"request {index}" })));
        try
        {
            Assert.Equal(3, responses.Count(response => response.StatusCode == HttpStatusCode.Accepted));
            Assert.Equal(9, responses.Count(response => response.StatusCode == HttpStatusCode.TooManyRequests));
            await using var context = scenario.Context();
            Assert.Equal(3, await context.ResearchRuns.CountAsync());
            Assert.Equal(3, await context.Jobs.CountAsync());
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task MatchingReplayStillSucceedsAtCapacityAndIncompatibleReuseStillConflicts()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database, services => services.PostConfigure<ResearchLimitOptions>(limits => limits.MaxOutstandingPerOwner = 1), useDefaultResearchLimits: true);
        var original = await ResearchTestData.SubmitAsync(scenario, "repeat");
        Assert.Equal(original.Id, (await ResearchTestData.SubmitAsync(scenario, "repeat")).Id);
        using var client = scenario.Client();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/archives/{scenario.ArchiveId}/research-jobs") { Content = JsonContent.Create(new { objective = "different" }) };
        request.Headers.Add("Idempotency-Key", "repeat");
        Assert.Equal(HttpStatusCode.Conflict, (await client.SendAsync(request)).StatusCode);
        using var viewer = scenario.Client("viewer");
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync($"/archives/{scenario.ArchiveId}/research-jobs", new { })).StatusCode);
        await using var context = scenario.Context();
        Assert.Single(await context.ResearchRuns.ToListAsync());
    }

    [Fact]
    public async Task LegacyIpRateLimitDoesNotRejectSubmissions()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database, services => services.PostConfigure<ResearchLimitOptions>(limits => limits.MaxSubmissionsPerIpPerMinute = 1), useDefaultResearchLimits: true);
        await ResearchTestData.SubmitAsync(scenario);
        using var client = scenario.Client();
        using var response = await client.PostAsJsonAsync($"/archives/{scenario.ArchiveId}/research-jobs", new { });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await using var context = scenario.Context();
        Assert.Equal(2, await context.Jobs.CountAsync());
    }

    [Fact]
    public async Task ConcurrentClaimsCannotExceedTheActiveAttemptLimit()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database, useDefaultResearchLimits: true);
        await ResearchTestData.SubmitAsync(scenario);
        await ResearchTestData.SubmitAsync(scenario);
        await using var scope = scenario.Scope();
        var queue = ResearchTestData.Queue(scope);
        var leases = await Task.WhenAll(queue.ClaimAsync("one", default), queue.ClaimAsync("two", default));
        Assert.Single(leases, lease => lease is not null);
        await using var context = scenario.Context();
        Assert.Single(await context.JobAttempts.ToListAsync());
        Assert.Equal(1, await context.ResearchRuns.CountAsync(run => run.Status == "queued"));
    }

    [Fact]
    public async Task LegacyDailyAttemptLimitsDoNotBlockRetries()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database, services => services.PostConfigure<ResearchLimitOptions>(limits =>
        { limits.MaxAttemptsGlobalPerDay = 1; limits.MaxAttemptsPerOwnerPerDay = 1; }), useDefaultResearchLimits: true);
        await ResearchTestData.SubmitAsync(scenario);
        await using var scope = scenario.Scope();
        var queue = ResearchTestData.Queue(scope);
        var first = (await queue.ClaimAsync("first", default))!;
        await queue.FailAsync(first, ResearchFailure.Unavailable, default);
        await ResearchTestData.ReadyAsync(scenario);
        Assert.NotNull(await queue.ClaimAsync("retry", default));
        await using var context = scenario.Context();
        Assert.Equal(2, await context.JobAttempts.CountAsync());
    }

    [Fact]
    public async Task AttemptTimeAndLeaseAreAssignedAfterWaitingForTheArchiveLock()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database, useDefaultResearchLimits: true);
        await ResearchTestData.SubmitAsync(scenario);
        await using var locked = scenario.Context();
        await using var transaction = await locked.Database.BeginTransactionAsync();
        await locked.Archives.FromSql($"SELECT * FROM research.archives WHERE id = {scenario.ArchiveId} FOR UPDATE").SingleAsync();
        await using var scope = scenario.Scope();
        var claim = ResearchTestData.Queue(scope).ClaimAsync("blocked-claim", default);
        DateTime release;
        try
        {
            await using var inspect = scenario.Context();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (await inspect.Database.SqlQuery<int>($"""
                SELECT count(*)::int AS "Value" FROM pg_stat_activity WHERE datname = current_database()
                AND wait_event_type = 'Lock' AND query LIKE '%research.archives%' AND query LIKE '%FOR UPDATE%'
                """).SingleAsync(deadline.Token) == 0) await Task.Delay(20, deadline.Token);
            release = await inspect.Database.SqlQuery<DateTime>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync();
        }
        finally { await transaction.CommitAsync(); }
        Assert.NotNull(await claim.WaitAsync(TimeSpan.FromSeconds(15)));
        await using var verify = scenario.Context();
        Assert.True((await verify.JobAttempts.SingleAsync()).StartedAt >= release);
        Assert.True((await verify.Jobs.SingleAsync()).LockedUntil >= release.AddSeconds(120));
    }

    [Fact]
    public async Task UnboundedDistinctKeysCannotFillAnArchiveQueue()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database,
            services => services.PostConfigure<ResearchLimitOptions>(limits => limits.MaxRunsPerOwnerPerMinute = 100), useDefaultResearchLimits: true);
        using var client = scenario.Client();
        // The default demonstration policy allows five outstanding jobs/archive.
        for (var index = 0; index < 5; index++)
            await ResearchTestData.SubmitAsync(scenario, $"accepted-{index}");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/archives/{scenario.ArchiveId}/research-jobs")
        { Content = JsonContent.Create(new { objective = "another key must not bypass capacity" }) };
        request.Headers.Add("Idempotency-Key", "a-new-key");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.NotNull(response.Headers.RetryAfter);
        await using var context = scenario.Context();
        Assert.Equal(5, await context.Jobs.CountAsync());
        Assert.Equal(5, await context.ResearchRuns.CountAsync());
    }
}
