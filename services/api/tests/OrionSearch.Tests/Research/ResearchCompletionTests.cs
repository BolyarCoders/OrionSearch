using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using OrionSearch.Application.Research;
using OrionSearch.Infrastructure.Research;
using OrionSearch.Tests.Archives;
using OrionSearch.Tests.Persistence;

namespace OrionSearch.Tests.Research;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class ResearchCompletionTests(PostgreSqlFixture database)
{
    [Fact]
    public Task LongIngestionRetainsOwnershipPastItsOriginalDeadline() => CompletePastDeadlineAsync(false, false);

    [Fact]
    public Task AHeartbeatWaitingOnCompletionStopsAfterCommit() => CompletePastDeadlineAsync(true, false);

    [Fact]
    public Task RecoveryCannotStealAnExpiredLeaseHeldByCompletion() => CompletePastDeadlineAsync(false, true);

    private async Task CompletePastDeadlineAsync(bool contendHeartbeat, bool recover)
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        var run = await ResearchTestData.SubmitAsync(scenario);
        await using var scope = scenario.Scope();
        var queue = ResearchTestData.Queue(scope);
        var lease = (await queue.ClaimAsync("completing-worker", default))!;
        await using var context = scenario.Context();
        await context.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION public.hold_ingestion() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN PERFORM pg_advisory_xact_lock(120012); RETURN NEW; END $$;
            CREATE TRIGGER hold_ingestion BEFORE INSERT ON content.documents
            FOR EACH ROW EXECUTE FUNCTION public.hold_ingestion();
            """);
        await using var barrier = new NpgsqlConnection(scenario.ConnectionString);
        await barrier.OpenAsync();
        await ExecuteAsync(barrier, "SELECT pg_advisory_lock(120012)");
        await context.Database.ExecuteSqlRawAsync("UPDATE system.jobs SET locked_until = clock_timestamp() + interval '5 seconds'");
        var completion = queue.CompleteAsync(lease, ResearchTestData.Output(run.Id), default);
        Task<bool>? heartbeat = null;
        try
        {
            await WaitAsync(async () => await context.Database.SqlQuery<int>($"""
                SELECT count(*)::integer AS "Value" FROM pg_stat_activity
                WHERE datname = current_database() AND wait_event = 'advisory'
                """).SingleAsync() == 1);
            Assert.True(await context.Database.SqlQuery<bool>($"""
                SELECT locked_until > clock_timestamp() AS "Value" FROM system.jobs WHERE id = {lease.JobId}
                """).SingleAsync());
            if (contendHeartbeat)
            {
                heartbeat = queue.HeartbeatAsync(lease, default);
                await WaitAsync(async () => await context.Database.SqlQuery<int>($"""
                    SELECT count(*)::integer AS "Value" FROM pg_stat_activity
                    WHERE datname = current_database() AND wait_event_type = 'Lock'
                        AND query LIKE 'UPDATE system.jobs SET locked_until%'
                    """).SingleAsync() == 1);
            }
            await WaitAsync(async () => await context.Database.SqlQuery<bool>($"""
                SELECT locked_until <= clock_timestamp() AS "Value" FROM system.jobs WHERE id = {lease.JobId}
                """).SingleAsync());
            Assert.False(completion.IsCompleted);
            if (recover) Assert.Equal(0, await queue.RecoverAsync(default).WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Empty(await context.Documents.ToListAsync());
            Assert.Equal("running", (await context.JobAttempts.AsNoTracking().SingleAsync()).Status);
        }
        finally { await ExecuteAsync(barrier, "SELECT pg_advisory_unlock(120012)"); }

        Assert.True(await completion.WaitAsync(TimeSpan.FromSeconds(15)));
        if (heartbeat is not null) Assert.False(await heartbeat.WaitAsync(TimeSpan.FromSeconds(15)));
        Assert.False(await queue.HeartbeatAsync(lease, default));
        Assert.Equal(0, await queue.RecoverAsync(default));
        Assert.Equal("completed", (await context.Jobs.AsNoTracking().SingleAsync()).Status);
        Assert.Equal("completed", (await context.ResearchRuns.AsNoTracking().SingleAsync()).Status);
        Assert.Equal("completed", (await context.JobAttempts.AsNoTracking().SingleAsync()).Status);
        Assert.Single(await context.Documents.ToListAsync());
        Assert.Single(await context.DocumentVersions.ToListAsync());
        Assert.Single(await context.Findings.ToListAsync());
        Assert.Single(await context.FindingEvidence.ToListAsync());
        Assert.False(await queue.CompleteAsync(lease, ResearchTestData.Output(run.Id), default));
    }

    [Theory]
    [InlineData("complete")]
    [InlineData("fail")]
    [InlineData("dispatch")]
    public async Task OwnershipMustStillBeLiveAfterWaitingForTheJobLock(string operation)
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        var run = await ResearchTestData.SubmitAsync(scenario);
        await using var scope = scenario.Scope();
        var queue = ResearchTestData.Queue(scope);
        var lease = (await queue.ClaimAsync("waiting-worker", default))!;
        await using var context = scenario.Context();
        await context.Database.ExecuteSqlRawAsync("UPDATE system.jobs SET locked_until = clock_timestamp() + interval '5 seconds'");
        await using var locked = scenario.Context();
        await using var transaction = await locked.Database.BeginTransactionAsync();
        await locked.Jobs.FromSql($"SELECT * FROM system.jobs WHERE id = {lease.JobId} FOR UPDATE").SingleAsync();
        var action = operation switch
        {
            "complete" => queue.CompleteAsync(lease, ResearchTestData.Output(run.Id), default),
            "fail" => queue.FailAsync(lease, ResearchFailure.Contract, default),
            _ => DispatchAsync()
        };
        try
        {
            await WaitAsync(async () => await context.Database.SqlQuery<int>($"""
                SELECT count(*)::integer AS "Value" FROM pg_stat_activity
                WHERE datname = current_database() AND wait_event_type = 'Lock'
                    AND query LIKE '%FOR UPDATE OF j%'
                """).SingleAsync() == 1);
            await WaitAsync(async () => await context.Database.SqlQuery<bool>($"""
                SELECT locked_until <= clock_timestamp() AS "Value" FROM system.jobs WHERE id = {lease.JobId}
                """).SingleAsync());
        }
        finally { await transaction.RollbackAsync(); }
        Assert.False(await action.WaitAsync(TimeSpan.FromSeconds(15)));
        Assert.Empty(await context.Documents.ToListAsync());
        Assert.Equal("processing", (await context.Jobs.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(1, await queue.RecoverAsync(default));

        async Task<bool> DispatchAsync() => await queue.DispatchAsync(lease, default) is not null;
    }

    [Fact]
    public async Task ProcessorSettlesAnInFlightHeartbeatBeforeEnteringCompletion()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        await ResearchTestData.SubmitAsync(scenario);
        await using var scope = scenario.Scope();
        var queue = new HeldHeartbeatQueue(ResearchTestData.Queue(scope));
        var response = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var agent = new FakeResearchAgent(async (request, ct) =>
        {
            await response.Task.WaitAsync(ct);
            return ResearchTestData.Output(request.ResearchRunId);
        });
        var processor = new ResearchProcessor(queue, agent,
            new ResearchWorkerOptions { Enabled = true, HeartbeatSeconds = 1 }, NullLogger<ResearchProcessor>.Instance);
        var processing = processor.ProcessOnceAsync("handoff-worker");
        try
        {
            await queue.Renewed.Task.WaitAsync(TimeSpan.FromSeconds(15));
            response.SetResult();
            await queue.StopRequested.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.False(queue.CompletionEntered.Task.IsCompleted);
        }
        finally
        {
            response.TrySetResult();
            queue.Release.TrySetResult();
            await processing.WaitAsync(TimeSpan.FromSeconds(15));
        }
        Assert.True(queue.Settled);
        Assert.Equal(1, queue.Heartbeats);
        Assert.True(queue.CompletionEntered.Task.IsCompleted);
        await using var context = scenario.Context();
        Assert.Equal("completed", (await context.ResearchRuns.SingleAsync()).Status);
        Assert.Single(await context.DocumentVersions.ToListAsync());
    }

    private static async Task WaitAsync(Func<Task<bool>> condition)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!await condition().WaitAsync(deadline.Token)) await Task.Delay(20, deadline.Token);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class HeldHeartbeatQueue(IResearchQueue inner) : IResearchQueue
    {
        public TaskCompletionSource Renewed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource StopRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CompletionEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Settled { get; private set; }
        public int Heartbeats { get; private set; }
        public Task<ResearchLease?> ClaimAsync(string id, CancellationToken ct) => inner.ClaimAsync(id, ct);
        public Task<AgentRequest?> DispatchAsync(ResearchLease lease, CancellationToken ct) => inner.DispatchAsync(lease, ct);
        public Task<int> RecoverAsync(CancellationToken ct) => inner.RecoverAsync(ct);
        public Task<bool> FailAsync(ResearchLease lease, ResearchFailure failure, CancellationToken ct, int? retryAfterSeconds = null) => inner.FailAsync(lease, failure, ct, retryAfterSeconds);
        public Task<bool> CompleteAsync(ResearchLease lease, AgentResult result, CancellationToken ct)
        {
            CompletionEntered.TrySetResult();
            return inner.CompleteAsync(lease, result, ct);
        }
        public async Task<bool> HeartbeatAsync(ResearchLease lease, CancellationToken ct)
        {
            Heartbeats++;
            var renewed = await inner.HeartbeatAsync(lease, ct);
            using var stop = ct.Register(() => StopRequested.TrySetResult());
            Renewed.TrySetResult();
            await Release.Task;
            Settled = true;
            return renewed;
        }
    }
}
