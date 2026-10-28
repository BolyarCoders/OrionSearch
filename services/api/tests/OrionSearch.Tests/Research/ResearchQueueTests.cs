using Microsoft.EntityFrameworkCore;
using OrionSearch.Application.Research;
using OrionSearch.Tests.Archives;
using OrionSearch.Tests.Persistence;

namespace OrionSearch.Tests.Research;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class ResearchQueueTests(PostgreSqlFixture database)
{
    [Fact]
    public async Task ConcurrentWorkersClaimDifferentAttempts()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        await ResearchTestData.SubmitAsync(scenario);
        await ResearchTestData.SubmitAsync(scenario);
        await using var scope = scenario.Scope();
        var queue = ResearchTestData.Queue(scope);
        var leases = await Task.WhenAll(queue.ClaimAsync("worker-one", default), queue.ClaimAsync("worker-two", default));
        Assert.All(leases, Assert.NotNull);
        Assert.Equal(2, leases.Select(lease => lease!.JobId).Distinct().Count());
        Assert.All(leases, lease => Assert.Equal(1, lease!.Attempt));
        await using var context = scenario.Context();
        Assert.Equal(2, await context.JobAttempts.CountAsync(attempt => attempt.Status == "running"));
        Assert.Equal(2, await context.Jobs.CountAsync(job => job.Status == "processing" && job.Attempts == 1));
        Assert.Null(await queue.ClaimAsync("worker-three", default));
    }

    [Fact]
    public async Task ClaimsSkipLockedRowsHonorPriorityAndIgnoreFutureJobs()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        var first = await ResearchTestData.SubmitAsync(scenario);
        var second = await ResearchTestData.SubmitAsync(scenario);
        var future = await ResearchTestData.SubmitAsync(scenario);
        var low = await ResearchTestData.SubmitAsync(scenario);
        await using (var context = scenario.Context())
        {
            await context.Jobs.Where(job => job.ResearchRunId == first.Id).ExecuteUpdateAsync(setters => setters.SetProperty(job => job.Priority, 10));
            await context.Jobs.Where(job => job.ResearchRunId == second.Id).ExecuteUpdateAsync(setters => setters.SetProperty(job => job.Priority, 5));
            await context.Jobs.Where(job => job.ResearchRunId == future.Id).ExecuteUpdateAsync(setters => setters
                .SetProperty(job => job.Priority, 100).SetProperty(job => job.RunAfter, DateTime.UtcNow.AddDays(1)));
        }
        await using var locked = scenario.Context();
        await using var transaction = await locked.Database.BeginTransactionAsync();
        await locked.Jobs.FromSql($"SELECT * FROM system.jobs WHERE research_run_id = {first.Id} FOR UPDATE").SingleAsync();
        await using var scope = scenario.Scope();
        var queue = ResearchTestData.Queue(scope);
        Assert.Equal(second.Id, (await queue.ClaimAsync("skip-worker", default))!.RunId);
        await transaction.RollbackAsync();
        Assert.Equal(first.Id, (await queue.ClaimAsync("priority-worker", default))!.RunId);
        Assert.Equal(low.Id, (await queue.ClaimAsync("low-worker", default))!.RunId);
        Assert.Null(await queue.ClaimAsync("future-worker", default));
    }

    [Fact]
    public async Task HeartbeatsRequireTheCurrentLiveLease()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        await ResearchTestData.SubmitAsync(scenario);
        await using var scope = scenario.Scope();
        var queue = ResearchTestData.Queue(scope);
        var lease = (await queue.ClaimAsync("live-worker", default))!;
        await using var context = scenario.Context();
        await context.Database.ExecuteSqlRawAsync("UPDATE system.jobs SET locked_until = clock_timestamp() + interval '5 seconds'");
        var before = (await context.Jobs.AsNoTracking().SingleAsync()).LockedUntil;
        Assert.False(await queue.HeartbeatAsync(lease with { WorkerId = "imposter" }, default));
        Assert.True(await queue.HeartbeatAsync(lease, default));
        Assert.True((await context.Jobs.AsNoTracking().SingleAsync()).LockedUntil > before);
        await ResearchTestData.ExpireAsync(scenario);
        Assert.False(await queue.HeartbeatAsync(lease, default));
    }

    [Fact]
    public async Task RecoveryIsConcurrentSafeAndFencesStaleCompletionFailureAndHeartbeat()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        var run = await ResearchTestData.SubmitAsync(scenario);
        await using var scope = scenario.Scope();
        var queue = ResearchTestData.Queue(scope);
        var abandoned = (await queue.ClaimAsync("old-worker", default))!;
        await ResearchTestData.ExpireAsync(scenario);
        Assert.False(await queue.CompleteAsync(abandoned, ResearchTestData.Output(run.Id), default));
        Assert.False(await queue.FailAsync(abandoned, ResearchFailure.Contract, default));
        Assert.False(await queue.HeartbeatAsync(abandoned, default));
        await using (var beforeRecovery = scenario.Context()) Assert.Empty(await beforeRecovery.Documents.ToListAsync());
        var recovered = await Task.WhenAll(queue.RecoverAsync(default), queue.RecoverAsync(default));
        Assert.Equal(1, recovered.Sum());
        await using var context = scenario.Context();
        Assert.Equal("timed_out", (await context.JobAttempts.SingleAsync()).Status);
        var pending = await context.Jobs.AsNoTracking().SingleAsync();
        Assert.Equal("pending", pending.Status);
        Assert.Equal(1, pending.Attempts);
        Assert.Null(pending.WorkerId);
        Assert.True(pending.RunAfter > DateTime.UtcNow);
        await ResearchTestData.ReadyAsync(scenario);
        var replacement = (await queue.ClaimAsync("new-worker", default))!;
        Assert.Equal(2, replacement.Attempt);
        Assert.False(await queue.CompleteAsync(abandoned, ResearchTestData.Output(run.Id), default));
        Assert.False(await queue.FailAsync(abandoned, ResearchFailure.Authentication, default));
        Assert.False(await queue.HeartbeatAsync(abandoned, default));
        Assert.True(await queue.CompleteAsync(replacement, ResearchTestData.Output(run.Id), default));
        Assert.False(await queue.CompleteAsync(replacement, ResearchTestData.Output(run.Id), default));
        Assert.False(await queue.FailAsync(abandoned, ResearchFailure.Contract, default));
        Assert.Equal("completed", (await context.ResearchRuns.AsNoTracking().SingleAsync()).Status);
        Assert.Single(await context.ResearchQueries.ToListAsync());
        Assert.Single(await context.Findings.ToListAsync());
        Assert.Single(await context.FindingEvidence.ToListAsync());
        Assert.Equal(new[] { "timed_out", "completed" }, await context.JobAttempts.OrderBy(attempt => attempt.AttemptNumber).Select(attempt => attempt.Status).ToArrayAsync());
    }

    [Theory]
    [InlineData(ResearchFailure.Timeout, true, "timed_out")]
    [InlineData(ResearchFailure.Unavailable, true, "failed")]
    [InlineData(ResearchFailure.Contract, false, "failed")]
    [InlineData(ResearchFailure.Authentication, false, "failed")]
    [InlineData(ResearchFailure.Persistence, false, "failed")]
    public async Task FailurePolicyPersistsSafeAttemptHistoryAndBoundedBackoff(ResearchFailure failure, bool retry, string attemptStatus)
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        await ResearchTestData.SubmitAsync(scenario);
        await using var scope = scenario.Scope();
        var queue = ResearchTestData.Queue(scope);
        var lease = (await queue.ClaimAsync("failure-worker", default))!;
        Assert.True(await queue.FailAsync(lease, failure, default));
        await using var context = scenario.Context();
        var job = await context.Jobs.SingleAsync();
        var run = await context.ResearchRuns.SingleAsync();
        Assert.Equal(retry ? "pending" : "failed", job.Status);
        Assert.Equal(retry ? "queued" : "failed", run.Status);
        Assert.Equal(attemptStatus, (await context.JobAttempts.SingleAsync()).Status);
        Assert.Equal(ResearchFailures.Summary(failure), run.ErrorMessage);
        Assert.Null(job.LockedUntil);
        Assert.Null(job.WorkerId);
        Assert.Equal(!retry, run.FinishedAt is not null);
        Assert.True(job.RunAfter <= DateTime.UtcNow.AddMinutes(5));
        if (retry) Assert.True(job.RunAfter > DateTime.UtcNow);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LastAttemptCannotBeRequeuedForever(bool recover)
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        await ResearchTestData.SubmitAsync(scenario);
        await using var scope = scenario.Scope();
        var queue = ResearchTestData.Queue(scope);
        var lease = (await queue.ClaimAsync("last-worker", default))!;
        await using var context = scenario.Context();
        await context.Jobs.ExecuteUpdateAsync(setters => setters.SetProperty(job => job.MaxAttempts, 1));
        if (recover)
        {
            await ResearchTestData.ExpireAsync(scenario);
            Assert.Equal(1, await queue.RecoverAsync(default));
        }
        else Assert.True(await queue.FailAsync(lease, ResearchFailure.Timeout, default));
        Assert.Equal("failed", (await context.Jobs.SingleAsync()).Status);
        Assert.Equal("failed", (await context.ResearchRuns.SingleAsync()).Status);
        Assert.Null(await queue.ClaimAsync("another-worker", default));
    }

    [Fact]
    public async Task FinalFenceRollsBackIngestionIfOwnershipChangesDuringWrites()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        var run = await ResearchTestData.SubmitAsync(scenario);
        await using var scope = scenario.Scope();
        var queue = ResearchTestData.Queue(scope);
        var lease = (await queue.ClaimAsync("expiring-worker", default))!;
        await using var context = scenario.Context();
        await context.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION public.change_owner_during_ingestion() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN UPDATE system.jobs SET worker_id = 'superseded' WHERE status = 'processing'; RETURN NEW; END $$;
            CREATE TRIGGER change_owner_during_ingestion BEFORE INSERT ON content.documents
            FOR EACH ROW EXECUTE FUNCTION public.change_owner_during_ingestion();
            """);
        Assert.False(await queue.CompleteAsync(lease, ResearchTestData.Output(run.Id), default));
        Assert.Empty(await context.Documents.ToListAsync());
        Assert.Empty(await context.Publishers.ToListAsync());
        Assert.Empty(await context.Findings.ToListAsync());
        Assert.Empty(await context.ResearchQueries.ToListAsync());
        Assert.Equal("running", (await context.ResearchRuns.SingleAsync()).Status);
        Assert.Equal("processing", (await context.Jobs.SingleAsync()).Status);
    }

    [Fact]
    public async Task PersistenceFailureCannotLeavePartialSourcesOrMarkSuccess()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        var run = await ResearchTestData.SubmitAsync(scenario);
        await using var scope = scenario.Scope();
        var queue = ResearchTestData.Queue(scope);
        var lease = (await queue.ClaimAsync("rollback-worker", default))!;
        await using var context = scenario.Context();
        await context.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION public.reject_evidence() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'private evidence failure'; END $$;
            CREATE TRIGGER reject_evidence BEFORE INSERT ON knowledge.finding_evidence FOR EACH ROW EXECUTE FUNCTION public.reject_evidence();
            """);
        await Assert.ThrowsAsync<DbUpdateException>(() => queue.CompleteAsync(lease, ResearchTestData.Output(run.Id), default));
        Assert.Empty(await context.Documents.ToListAsync());
        Assert.Empty(await context.DocumentVersions.ToListAsync());
        Assert.Empty(await context.DocumentChunks.ToListAsync());
        Assert.Empty(await context.ArchiveDocuments.ToListAsync());
        Assert.Empty(await context.Findings.ToListAsync());
        Assert.Empty(await context.ResearchQueries.ToListAsync());
        Assert.Equal("running", (await context.ResearchRuns.SingleAsync()).Status);
        Assert.Equal("running", (await context.JobAttempts.SingleAsync()).Status);
        await ResearchTestData.ExpireAsync(scenario);
        Assert.Equal(1, await queue.RecoverAsync(default));
        await ResearchTestData.ReadyAsync(scenario);
        await context.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_evidence ON knowledge.finding_evidence");
        var replacement = (await queue.ClaimAsync("replacement-worker", default))!;
        Assert.True(await queue.CompleteAsync(replacement, ResearchTestData.Output(run.Id), default));
        Assert.Single(await context.DocumentVersions.ToListAsync());
        Assert.Single(await context.Findings.ToListAsync());
        Assert.Single(await context.FindingEvidence.ToListAsync());
        Assert.Equal("completed", (await context.ResearchRuns.AsNoTracking().SingleAsync()).Status);
    }
}
