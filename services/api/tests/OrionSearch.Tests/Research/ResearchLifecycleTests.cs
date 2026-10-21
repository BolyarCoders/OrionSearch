using Microsoft.EntityFrameworkCore;
using OrionSearch.Application.Research;
using OrionSearch.Tests.Archives;
using OrionSearch.Tests.Persistence;

namespace OrionSearch.Tests.Research;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class ResearchLifecycleTests(PostgreSqlFixture database)
{
    [Fact]
    public async Task PausedPendingWorkWaitsUntilReactivated()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        await ResearchTestData.SubmitAsync(scenario);
        await using var context = scenario.Context();
        await context.Archives.ExecuteUpdateAsync(setters => setters.SetProperty(archive => archive.Status, "paused"));
        await using var scope = scenario.Scope();
        var queue = ResearchTestData.Queue(scope);
        Assert.Null(await queue.ClaimAsync("paused-worker", default));
        Assert.Empty(await context.JobAttempts.ToListAsync());
        Assert.Equal(0, await queue.RecoverAsync(default));
        await context.Archives.ExecuteUpdateAsync(setters => setters.SetProperty(archive => archive.Status, "active"));
        Assert.NotNull(await queue.ClaimAsync("reactivated-worker", default));
    }

    [Fact]
    public async Task ArchivedPendingWorkCancelsWithoutAnAttemptOrDataDeletion()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        await ResearchTestData.SubmitAsync(scenario);
        await using var context = scenario.Context();
        var retained = await TestData.CreateVersionAsync(context);
        await context.Archives.ExecuteUpdateAsync(setters => setters.SetProperty(archive => archive.Status, "archived"));
        await using var scope = scenario.Scope();
        var queue = ResearchTestData.Queue(scope);
        Assert.Null(await queue.ClaimAsync("archived-worker", default));
        Assert.Equal(1, await queue.RecoverAsync(default));
        Assert.Equal("cancelled", (await context.Jobs.SingleAsync()).Status);
        Assert.Equal("cancelled", (await context.ResearchRuns.SingleAsync()).Status);
        Assert.Empty(await context.JobAttempts.ToListAsync());
        Assert.True(await context.DocumentVersions.AnyAsync(version => version.Id == retained.Id));
    }

    [Fact]
    public async Task ArchivedInFlightWorkCannotCommitLateResults()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        var run = await ResearchTestData.SubmitAsync(scenario);
        await using var scope = scenario.Scope();
        var queue = ResearchTestData.Queue(scope);
        var lease = (await queue.ClaimAsync("late-worker", default))!;
        Assert.NotNull(await queue.DispatchAsync(lease, default));
        await using var context = scenario.Context();
        await context.Archives.ExecuteUpdateAsync(setters => setters.SetProperty(archive => archive.Status, "archived"));
        Assert.False(await queue.CompleteAsync(lease, ResearchTestData.Output(run.Id), default));
        Assert.Empty(await context.Documents.ToListAsync());
        Assert.Empty(await context.ResearchQueries.ToListAsync());
        Assert.Equal("cancelled", (await context.ResearchRuns.SingleAsync()).Status);
        Assert.False(await queue.HeartbeatAsync(lease, default));
    }

    [Theory]
    [InlineData("paused", "queued")]
    [InlineData("archived", "cancelled")]
    public async Task DispatchRechecksLifecycleAfterClaim(string archiveState, string runState)
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        await ResearchTestData.SubmitAsync(scenario);
        await using var scope = scenario.Scope();
        var queue = ResearchTestData.Queue(scope);
        var lease = (await queue.ClaimAsync("dispatch-worker", default))!;
        await using var context = scenario.Context();
        await context.Archives.ExecuteUpdateAsync(setters => setters.SetProperty(archive => archive.Status, archiveState));
        Assert.Null(await queue.DispatchAsync(lease, default));
        Assert.Equal(runState, (await context.ResearchRuns.SingleAsync()).Status);
        Assert.Equal("failed", (await context.JobAttempts.SingleAsync()).Status);
    }

    [Fact]
    public async Task PausingAllowsAnAlreadyDispatchedAttemptToFinish()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        var run = await ResearchTestData.SubmitAsync(scenario);
        await using var scope = scenario.Scope();
        var queue = ResearchTestData.Queue(scope);
        var lease = (await queue.ClaimAsync("in-flight-worker", default))!;
        Assert.NotNull(await queue.DispatchAsync(lease, default));
        await using var context = scenario.Context();
        await context.Archives.ExecuteUpdateAsync(setters => setters.SetProperty(archive => archive.Status, "paused"));
        Assert.True(await queue.CompleteAsync(lease, ResearchTestData.Output(run.Id), default));
        Assert.Equal("completed", (await context.ResearchRuns.SingleAsync()).Status);
    }
}
