using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OrionSearch.Application.Archives;
using OrionSearch.Application.Research;
using OrionSearch.Domain.Content;
using OrionSearch.Infrastructure.Research;
using OrionSearch.Tests.Archives;
using OrionSearch.Tests.Persistence;

namespace OrionSearch.Tests.Research;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class ResearchProcessingTests(PostgreSqlFixture database)
{
    [Fact]
    public async Task ProcessingRunsOutsideTheRequestAndCommitsSourcesAndEvidenceBeforeSuccess()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        var entered = new TaskCompletionSource<AgentRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var agent = new FakeResearchAgent(async (request, ct) =>
        {
            entered.SetResult(request);
            await release.Task.WaitAsync(ct);
            var result = ResearchTestData.Output(request.ResearchRunId);
            return result with { Sources = [.. result.Sources, new SourceOutput("discovered", "https://example.com/discovered")] };
        });
        var submitted = await ResearchTestData.SubmitAsync(scenario);
        Assert.Equal(0, agent.Calls);
        Assert.Equal("queued", submitted.Status);
        await using var scope = scenario.Scope();
        var processor = Processor(ResearchTestData.Queue(scope), agent);
        var processing = processor.ProcessOnceAsync("processor-test");
        try
        {
            var request = await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(submitted.Id, request.ResearchRunId);
            Assert.Equal(scenario.ArchiveId, request.ArchiveId);
            Assert.Equal("Existing archive", request.Title);
            using var client = scenario.Client();
            var running = (await client.GetFromJsonAsync<ResearchStatus>($"/research-jobs/{submitted.Id}"))!;
            Assert.Equal("running", running.Status);
            using var update = await client.PatchAsJsonAsync($"/archives/{scenario.ArchiveId}", new { title = "Changed during external execution" });
            Assert.Equal(HttpStatusCode.OK, update.StatusCode);
            await using var context = scenario.Context();
            var openTransactions = await context.Database.SqlQuery<int>($"""
                SELECT count(*)::integer AS "Value" FROM pg_stat_activity
                WHERE datname = current_database() AND state = 'idle in transaction'
                """).SingleAsync();
            Assert.Equal(0, openTransactions);
            Assert.Empty(await context.Documents.ToListAsync());
            var initialLease = (await context.Jobs.AsNoTracking().SingleAsync()).LockedUntil;
            using var renewalDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while ((await context.Jobs.AsNoTracking().SingleAsync(renewalDeadline.Token)).LockedUntil <= initialLease)
                await Task.Delay(50, renewalDeadline.Token);
            Assert.False(processing.IsCompleted);
        }
        finally { release.TrySetResult(); }
        Assert.True(await processing);
        Assert.Equal(1, agent.Calls);
        using var reader = scenario.Client("viewer");
        var completed = (await reader.GetFromJsonAsync<ResearchStatus>($"/research-jobs/{submitted.Id}"))!;
        Assert.Equal("completed", completed.Status);
        Assert.NotNull(completed.StartedAt);
        Assert.NotNull(completed.FinishedAt);
        Assert.Equal(new RunStatistics(1, 2, 1, 1), completed.Statistics);
        var sources = (await reader.GetFromJsonAsync<Page<SourceSummary>>($"/archives/{scenario.ArchiveId}/sources"))!;
        Assert.Equal(2, sources.TotalCount);
        Assert.All(sources.Items, source => Assert.Null(source.PublishedAt));
        await using var stored = scenario.Context();
        var version = await stored.DocumentVersions.SingleAsync();
        var chunk = await stored.DocumentChunks.SingleAsync();
        var finding = await stored.Findings.SingleAsync();
        var evidence = await stored.FindingEvidence.SingleAsync();
        Assert.Equal(ContentFingerprint.Compute("Research preserves evidence."), version.ContentHash);
        Assert.Equal(version.Id, chunk.VersionId);
        Assert.Equal(version.Id, evidence.DocumentVersionId);
        Assert.Equal(chunk.Id, evidence.ChunkId);
        Assert.Equal(submitted.Id, finding.ResearchRunId);
        Assert.Equal("unverified", finding.Status);
        Assert.Equal(submitted.Id, (await stored.ResearchQueries.SingleAsync()).RunId);
        Assert.Equal("completed", (await stored.Jobs.SingleAsync()).Status);
        Assert.Equal("completed", (await stored.JobAttempts.SingleAsync()).Status);
    }

    [Fact]
    public async Task DisabledOrUnconfiguredProcessingCannotClaimOrInventResults()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        await ResearchTestData.SubmitAsync(scenario);
        await using var scope = scenario.Scope();
        var queue = ResearchTestData.Queue(scope);
        var fake = new FakeResearchAgent((request, _) => Task.FromResult(ResearchTestData.Output(request.ResearchRunId)));
        var disabled = new ResearchProcessor(queue, fake, new ResearchWorkerOptions(), NullLogger<ResearchProcessor>.Instance);
        Assert.False(await disabled.ProcessOnceAsync("disabled"));
        var unconfigured = new FakeResearchAgent((_, _) => throw new InvalidOperationException("must never execute"), false);
        Assert.False(await Processor(queue, unconfigured).ProcessOnceAsync("unconfigured"));
        Assert.Equal(0, fake.Calls);
        await using var context = scenario.Context();
        Assert.Empty(await context.JobAttempts.ToListAsync());
        Assert.Empty(await context.Documents.ToListAsync());
        Assert.Equal("queued", (await context.ResearchRuns.SingleAsync()).Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ContractFailuresArePermanentAndTimeoutsRetryWithoutPartialResults(bool malformed)
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        var submitted = await ResearchTestData.SubmitAsync(scenario);
        var agent = new FakeResearchAgent((request, _) => malformed
            ? Task.FromResult(ResearchTestData.Output(request.ResearchRunId) with { ContractVersion = "unsupported" })
            : throw new ResearchAgentException(ResearchFailure.Timeout));
        await using var scope = scenario.Scope();
        Assert.True(await Processor(ResearchTestData.Queue(scope), agent).ProcessOnceAsync("error-worker"));
        using var client = scenario.Client();
        var status = (await client.GetFromJsonAsync<ResearchStatus>($"/research-jobs/{submitted.Id}"))!;
        Assert.Equal(malformed ? "failed" : "queued", status.Status);
        Assert.Equal(!malformed, status.Retrying);
        Assert.Equal(ResearchFailures.Summary(malformed ? ResearchFailure.Contract : ResearchFailure.Timeout), status.Error);
        await using var context = scenario.Context();
        Assert.Empty(await context.Documents.ToListAsync());
    }

    [Fact]
    public async Task ConcurrentArchivesReuseGlobalSourcesVersionsAndChunks()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        var first = await ResearchTestData.SubmitAsync(scenario);
        using var client = scenario.Client();
        using var created = await client.PostAsJsonAsync("/archives", new { title = "Second archive" });
        var other = (await created.Content.ReadFromJsonAsync<ArchiveDetails>())!.Archive.Id;
        using var queued = await client.PostAsJsonAsync($"/archives/{other}/research-jobs", new { });
        var second = (await queued.Content.ReadFromJsonAsync<ResearchStatus>())!;
        await using var scope = scenario.Scope();
        var queue = ResearchTestData.Queue(scope);
        var leases = new[] { (await queue.ClaimAsync("one", default))!, (await queue.ClaimAsync("two", default))! };
        Assert.All(await Task.WhenAll(leases.Select(lease => queue.CompleteAsync(lease, ResearchTestData.Output(lease.RunId), default))), Assert.True);
        await using var context = scenario.Context();
        Assert.Single(await context.Publishers.ToListAsync());
        Assert.Single(await context.Documents.ToListAsync());
        Assert.Single(await context.DocumentVersions.ToListAsync());
        Assert.Single(await context.DocumentChunks.ToListAsync());
        Assert.Equal(2, await context.ArchiveDocuments.CountAsync());
        Assert.Equal(2, await context.ResearchQueries.CountAsync());
        Assert.Equal(new[] { first.Id, second.Id }.Order(), await context.Findings.Select(finding => finding.ResearchRunId!.Value).Order().ToArrayAsync());
        Assert.Equal(2, await context.FindingEvidence.CountAsync());
        Assert.Equal("https://example.com/source", (await context.Documents.SingleAsync()).CanonicalUrl);
        Assert.Equal("example.com", (await context.Publishers.SingleAsync()).Domain);
    }

    [Fact]
    public async Task RawOnlyOutputRetainsRealHashBasisAndNeverCreatesInventedText()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        var run = await ResearchTestData.SubmitAsync(scenario);
        var hash = ContentFingerprint.ComputeRaw("preserved raw bytes"u8);
        var output = ResearchTestData.Output(run.Id);
        output = output with
        {
            Sources = [output.Sources[0] with { Capture = new CaptureOutput(DateTime.UtcNow, Raw: new RawCaptureOutput("urn:orion:test:durable-raw", hash)) }],
            Findings = [new FindingOutput("raw", "A raw capture was discovered", "observation", [new EvidenceOutput("source-1", "mentions")])]
        };
        await using var scope = scenario.Scope();
        var queue = ResearchTestData.Queue(scope);
        var lease = (await queue.ClaimAsync("raw-worker", default))!;
        Assert.True(await queue.CompleteAsync(lease, output, default));
        await using var context = scenario.Context();
        var version = await context.DocumentVersions.SingleAsync();
        Assert.Null(version.ExtractedText);
        Assert.Equal(hash, version.ContentHash);
        Assert.Equal("urn:orion:test:durable-raw", version.StorageUri);
        Assert.Equal("raw_bytes", JsonDocument.Parse(version.Metadata).RootElement.GetProperty("content_hash").GetProperty("basis").GetString());
        Assert.Empty(await context.DocumentChunks.ToListAsync());
        Assert.Null((await context.FindingEvidence.SingleAsync()).ChunkId);
    }

    [Fact]
    public async Task StatusDoesNotExposePrivateErrorOrStatisticsFields()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        var submitted = await ResearchTestData.SubmitAsync(scenario);
        await using var context = scenario.Context();
        await context.ResearchRuns.ExecuteUpdateAsync(setters => setters
            .SetProperty(run => run.ErrorMessage, "private-credential-and-stack")
            .SetProperty(run => run.Stats, "{\"sources\":\"private-model-key\",\"queries\":1,\"privateConfig\":\"secret\"}"));
        using var client = scenario.Client();
        using var response = await client.GetAsync($"/research-jobs/{submitted.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", body, StringComparison.OrdinalIgnoreCase);
        var status = (await response.Content.ReadFromJsonAsync<ResearchStatus>())!;
        Assert.Equal(new RunStatistics(1), status.Statistics);
        Assert.Equal("Research did not complete.", status.Error);
    }

    [Fact]
    public async Task AddingRawStorageToAnExistingTextVersionRetainsItsDigestAndOriginalMetadata()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        var first = await ResearchTestData.SubmitAsync(scenario);
        await using var scope = scenario.Scope();
        var queue = ResearchTestData.Queue(scope);
        Assert.True(await queue.CompleteAsync((await queue.ClaimAsync("text", default))!, ResearchTestData.Output(first.Id), default));
        var second = await ResearchTestData.SubmitAsync(scenario);
        var output = ResearchTestData.Output(second.Id);
        var rawHash = ContentFingerprint.ComputeRaw("<html>Research preserves evidence.</html>"u8);
        var capture = output.Sources[0].Capture! with
        {
            Raw = new RawCaptureOutput("urn:orion:test:raw-html", rawHash),
            Metadata = JsonSerializer.SerializeToElement(new { producer = "later-capture" })
        };
        output = output with { Sources = [output.Sources[0] with { Capture = capture }] };
        Assert.True(await queue.CompleteAsync((await queue.ClaimAsync("raw", default))!, output, default));
        await using var context = scenario.Context();
        var version = await context.DocumentVersions.SingleAsync();
        using var metadata = JsonDocument.Parse(version.Metadata);
        Assert.Equal("urn:orion:test:raw-html", version.StorageUri);
        Assert.Equal(rawHash, metadata.RootElement.GetProperty("raw_sha256").GetString());
        Assert.Equal("test", metadata.RootElement.GetProperty("producer").GetString());
        Assert.Equal("normalized_extracted_text", metadata.RootElement.GetProperty("content_hash").GetProperty("basis").GetString());
    }

    [Fact]
    public async Task ShutdownLeavesAnUncommittedAttemptForLeaseRecovery()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        await ResearchTestData.SubmitAsync(scenario);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var agent = new FakeResearchAgent(async (request, ct) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return ResearchTestData.Output(request.ResearchRunId);
        });
        await using var scope = scenario.Scope();
        var queue = ResearchTestData.Queue(scope);
        using var shutdown = new CancellationTokenSource();
        var processing = Processor(queue, agent).ProcessOnceAsync("shutdown-worker", shutdown.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await shutdown.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);
        await using var context = scenario.Context();
        Assert.Empty(await context.Documents.ToListAsync());
        Assert.Equal("processing", (await context.Jobs.AsNoTracking().SingleAsync()).Status);
        Assert.Equal("running", (await context.JobAttempts.AsNoTracking().SingleAsync()).Status);
        await ResearchTestData.ExpireAsync(scenario);
        Assert.Equal(1, await queue.RecoverAsync(default));
        Assert.Equal("pending", (await context.Jobs.AsNoTracking().SingleAsync()).Status);
        Assert.Equal("timed_out", (await context.JobAttempts.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task ConflictingChunkDefinitionsCannotReuseAnotherRunsEvidence()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        var first = await ResearchTestData.SubmitAsync(scenario);
        await using var scope = scenario.Scope();
        var queue = ResearchTestData.Queue(scope);
        var firstLease = (await queue.ClaimAsync("first", default))!;
        Assert.True(await queue.CompleteAsync(firstLease, ResearchTestData.Output(first.Id), default));
        var second = await ResearchTestData.SubmitAsync(scenario);
        var secondLease = (await queue.ClaimAsync("second", default))!;
        var output = ResearchTestData.Output(second.Id);
        var capture = output.Sources[0].Capture! with { Chunks = [new ChunkOutput(0, "Research", 0, 8)] };
        output = output with { Sources = [output.Sources[0] with { Capture = capture }], Findings = [] };
        var failure = await Assert.ThrowsAsync<ResearchAgentException>(() => queue.CompleteAsync(secondLease, output, default));
        Assert.Equal(ResearchFailure.Contract, failure.Failure);
        await using var context = scenario.Context();
        Assert.Single(await context.ResearchQueries.ToListAsync());
        Assert.Single(await context.Findings.ToListAsync());
        Assert.Single(await context.FindingEvidence.ToListAsync());
        Assert.Equal("Research preserves evidence.", (await context.DocumentChunks.SingleAsync()).Content);
        Assert.Equal("running", (await context.ResearchRuns.SingleAsync(run => run.Id == second.Id)).Status);
    }

    private static ResearchProcessor Processor(IResearchQueue queue, IResearchAgentClient agent) =>
        new(queue, agent, new ResearchWorkerOptions { Enabled = true, HeartbeatSeconds = 1, TimeoutSeconds = 30 }, NullLogger<ResearchProcessor>.Instance);
}
