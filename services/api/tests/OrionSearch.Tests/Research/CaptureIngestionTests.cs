using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrionSearch.Application.Content;
using Microsoft.Extensions.Logging.Abstractions;
using OrionSearch.Infrastructure.Research;
using OrionSearch.Domain.Content;
using OrionSearch.Application.Research;
using OrionSearch.Tests.Archives;
using OrionSearch.Tests.Persistence;

namespace OrionSearch.Tests.Research;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class CaptureIngestionTests(PostgreSqlFixture database)
{
    [Fact]
    public async Task HeartbeatContinuesWhileObjectBytesAreBeingVerified()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var scenario = await ArchiveApiScenario.CreateAsync(database,
            services => services.AddSingleton<ICaptureStore>(new BlockingStore(entered, release)));
        var run = await ResearchTestData.SubmitAsync(scenario);
        await using var scope = scenario.Scope();
        var output = ResearchTestData.Output(run.Id);
        output = output with
        {
            Sources = [output.Sources[0] with { Capture = output.Sources[0].Capture! with
        { Raw = new RawCaptureOutput("urn:test:blocked", ContentFingerprint.ComputeRaw("test"u8)) } }]
        };
        var agent = new FakeResearchAgent((_, _) => Task.FromResult(output));
        var processor = new ResearchProcessor(ResearchTestData.Queue(scope), agent,
            new ResearchWorkerOptions { Enabled = true, HeartbeatSeconds = 1 }, NullLogger<ResearchProcessor>.Instance);
        var processing = processor.ProcessOnceAsync("capture-heartbeat");
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await using var context = scenario.Context();
            var initial = (await context.Jobs.AsNoTracking().SingleAsync()).LockedUntil;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while ((await context.Jobs.AsNoTracking().SingleAsync(deadline.Token)).LockedUntil <= initial)
                await Task.Delay(50, deadline.Token);
            Assert.False(processing.IsCompleted);
        }
        finally { release.TrySetResult(); }
        Assert.True(await processing);
        await using var verify = scenario.Context();
        Assert.Equal("completed", (await verify.ResearchRuns.SingleAsync()).Status);
        Assert.Single(await verify.ArchiveDocumentVersions.ToListAsync());
    }

    [Fact]
    public async Task ObjectVerificationHoldsNoTransactionAndCannotCommitAfterLeaseReassignment()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new BlockingStore(entered, release);
        await using var scenario = await ArchiveApiScenario.CreateAsync(database, services => services.AddSingleton<ICaptureStore>(store));
        var run = await ResearchTestData.SubmitAsync(scenario);
        await using var scope = scenario.Scope();
        var queue = ResearchTestData.Queue(scope);
        var lease = (await queue.ClaimAsync("stale-capture", default))!;
        var output = ResearchTestData.Output(run.Id);
        var source = output.Sources[0];
        output = output with { Sources = [source with { Capture = source.Capture! with { Raw = new RawCaptureOutput("urn:test:blocked", new string('a', 64)) } }] };
        var completion = queue.CompleteAsync(lease, output, default);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await using var context = scenario.Context();
            Assert.Equal(0, await context.Database.SqlQuery<int>($"""
                SELECT count(*)::integer AS "Value" FROM pg_stat_activity
                WHERE datname = current_database() AND state = 'idle in transaction'
                """).SingleAsync());
            await ResearchTestData.ExpireAsync(scenario);
            Assert.Equal(1, await queue.RecoverAsync(default));
            await ResearchTestData.ReadyAsync(scenario);
            var replacement = (await queue.ClaimAsync("replacement", default))!;
            Assert.Equal(lease.Attempt + 1, replacement.Attempt);
        }
        finally { release.TrySetResult(); }
        Assert.False(await completion);
        await using var verify = scenario.Context();
        Assert.Empty(await verify.DocumentVersions.ToListAsync());
        Assert.Empty(await verify.ArchiveDocumentVersions.ToListAsync());
    }

    [Fact]
    public async Task UnverifiedRawObjectsCannotBeCommitted()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        var run = await ResearchTestData.SubmitAsync(scenario);
        await using var scope = scenario.Scope();
        var queue = ResearchTestData.Queue(scope);
        var lease = (await queue.ClaimAsync("capture-test", default))!;
        var output = ResearchTestData.Output(run.Id);
        var source = output.Sources[0];
        output = output with
        {
            Sources = [source with { Capture = source.Capture! with
        {
            Raw = new RawCaptureOutput("s3://untrusted-bucket/missing", new string('a', 64))
        } }]
        };
        await Assert.ThrowsAsync<ResearchAgentException>(() => queue.CompleteAsync(lease, output, default));
        await using var context = scenario.Context();
        Assert.Empty(await context.DocumentVersions.ToListAsync());
        Assert.Equal("running", (await context.ResearchRuns.SingleAsync()).Status);
    }

    private sealed class BlockingStore(TaskCompletionSource entered, TaskCompletionSource release) : ICaptureStore
    {
        public async Task<CaptureBytes> ReadAsync(string storageUri, string sha256, Guid? runId, CancellationToken ct)
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(ct);
            return new CaptureBytes("test"u8.ToArray(), "text/plain");
        }
    }
}
