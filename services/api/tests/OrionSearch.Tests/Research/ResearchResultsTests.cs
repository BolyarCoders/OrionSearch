using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OrionSearch.Application.Archives;
using OrionSearch.Application.Research;
using OrionSearch.Domain.Content;
using OrionSearch.Domain.Knowledge;
using OrionSearch.Domain.Research;
using OrionSearch.Tests.Archives;
using OrionSearch.Tests.Persistence;

namespace OrionSearch.Tests.Research;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class ResearchResultsTests(PostgreSqlFixture database)
{
    [Fact]
    public async Task HistoryFiltersAndPaginatesBeforeReturningSafeOrderedStatuses()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        var completed = await ResearchTestData.SubmitAsync(scenario);
        await CompleteAsync(scenario, completed.Id);
        var failed = await ResearchTestData.SubmitAsync(scenario);
        await using (var scope = scenario.Scope())
        {
            var queue = ResearchTestData.Queue(scope);
            Assert.True(await queue.FailAsync((await queue.ClaimAsync("failed", default))!, ResearchFailure.Contract, default));
        }
        await ResearchTestData.SubmitAsync(scenario);
        await using var context = scenario.Context();
        var legacy = new ResearchRun { ArchiveId = scenario.ArchiveId, Status = "completed", ErrorMessage = "private-exception-secret", Stats = "{\"queries\":2,\"sources\":-1,\"secret\":\"do-not-expose\"}" };
        context.ResearchRuns.Add(legacy);
        var other = await TestData.CreateArchiveAsync(context);
        context.ResearchRuns.Add(new ResearchRun { ArchiveId = other.Id });
        await context.SaveChangesAsync();
        var time = new DateTime(2026, 10, 29, 0, 0, 0, DateTimeKind.Utc);
        await context.ResearchRuns.ExecuteUpdateAsync(setters => setters.SetProperty(run => run.CreatedAt, time));
        var expected = await context.ResearchRuns.Where(run => run.ArchiveId == scenario.ArchiveId).OrderBy(run => run.Id).Select(run => run.Id).ToArrayAsync();
        var path = $"/archives/{scenario.ArchiveId}/research-jobs";
        foreach (var role in new[] { "owner", "editor", "viewer" })
        {
            using var client = scenario.Client(role);
            var all = (await client.GetFromJsonAsync<Page<ResearchStatus>>(path))!;
            Assert.Equal(expected, all.Items.Select(run => run.Id));
            Assert.Equal(4, all.TotalCount);
            var second = (await client.GetFromJsonAsync<Page<ResearchStatus>>(path + "?page=2&pageSize=1"))!;
            Assert.Equal(expected[1], Assert.Single(second.Items).Id);
            Assert.Equal(4, second.TotalCount);
            var successes = (await client.GetFromJsonAsync<Page<ResearchStatus>>(path + "?status=completed"))!;
            Assert.Equal(2, successes.TotalCount);
            var old = successes.Items.Single(run => run.Id == legacy.Id);
            Assert.Equal(0, old.Attempts);
            Assert.Equal(new RunStatistics(2), old.Statistics);
            Assert.Equal("Research did not complete.", old.Error);
            using var response = await client.GetAsync(path);
            Assert.True(response.Headers.CacheControl?.NoStore);
            var json = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("private", json);
            Assert.DoesNotContain("secret", json);
            Assert.DoesNotContain("workerId", json);
            Assert.DoesNotContain("payload", json);
        }
    }

    [Fact]
    public async Task FindingReadsRequireRunProvenanceAndExactVersionGrants()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        var run = await ResearchTestData.SubmitAsync(scenario);
        await CompleteAsync(scenario, run.Id);
        await using var context = scenario.Context();
        var own = await context.Findings.SingleAsync();
        var version = await context.DocumentVersions.SingleAsync();
        var original = await context.FindingEvidence.SingleAsync();
        var ungranted = await TestData.CreateVersionAsync(context, "Private global version");
        context.ArchiveDocuments.Add(new ArchiveDocument { ArchiveId = scenario.ArchiveId, DocumentId = ungranted.DocumentId });
        context.FindingEvidence.Add(new FindingEvidence { FindingId = own.Id, DocumentVersionId = ungranted.Id, EvidenceText = "Private global version" });
        context.Findings.Add(new Finding { Statement = "Legacy finding without provenance" });
        var queued = new ResearchRun { ArchiveId = scenario.ArchiveId };
        context.ResearchRuns.Add(queued);
        await context.SaveChangesAsync();
        context.Findings.Add(new Finding { Statement = "Not a completed result", ResearchRunId = queued.Id });
        var other = await TestData.CreateArchiveAsync(context);
        var otherRun = new ResearchRun { ArchiveId = other.Id, Status = "completed" };
        context.ResearchRuns.Add(otherRun);
        await context.SaveChangesAsync();
        var foreign = new Finding { Statement = "Other archive finding", ResearchRunId = otherRun.Id };
        context.Findings.Add(foreign);
        await context.SaveChangesAsync();
        var path = $"/archives/{scenario.ArchiveId}/findings";
        foreach (var role in new[] { "owner", "editor", "viewer" })
        {
            using var client = scenario.Client(role);
            var page = (await client.GetFromJsonAsync<Page<FindingSummary>>(path))!;
            var summary = Assert.Single(page.Items);
            Assert.Equal(1, page.TotalCount);
            Assert.Equal(own.Id, summary.Id);
            Assert.Equal("unverified", summary.Status);
            Assert.Equal(1, summary.EvidenceCount);
            var detail = (await client.GetFromJsonAsync<FindingDetails>(path + $"/{own.Id}"))!;
            var evidence = Assert.Single(detail.Evidence.Items);
            Assert.Equal(original.Id, evidence.Id);
            Assert.Equal(version.Id, evidence.VersionId);
            Assert.Equal(version.DocumentId, evidence.DocumentId);
            Assert.Equal(0, evidence.ChunkIndex);
            Assert.Equal("preserves evidence", evidence.Quote);
            Assert.False(evidence.QuoteTruncated);
            Assert.Equal(1, detail.Evidence.TotalCount);
            Assert.Empty((await client.GetFromJsonAsync<FindingDetails>(path + $"/{own.Id}?page=2&pageSize=1"))!.Evidence.Items);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path + $"/{foreign.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path + $"?runId={otherRun.Id}")).StatusCode);
            Assert.Single((await client.GetFromJsonAsync<Page<FindingSummary>>(path + $"?runId={run.Id}&type=observation&status=unverified"))!.Items);
            Assert.Empty((await client.GetFromJsonAsync<Page<FindingSummary>>(path + "?type=claim"))!.Items);
            var json = await client.GetStringAsync(path + $"/{own.Id}");
            Assert.DoesNotContain("Private global", json);
            Assert.DoesNotContain("storageUri", json);
        }
    }

    [Fact]
    public async Task FindingFiltersStablePaginationAndLegacyTextBoundsAreAppliedInSql()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        var run = await ResearchTestData.SubmitAsync(scenario);
        await CompleteAsync(scenario, run.Id);
        await using var context = scenario.Context();
        var original = await context.Findings.SingleAsync();
        var extra = new Finding { ResearchRunId = run.Id, Statement = new string('s', 12000), FindingType = "claim", Status = "disputed" };
        var second = new Finding { ResearchRunId = run.Id, Statement = "Another disputed claim", FindingType = "claim", Status = "disputed" };
        context.Findings.AddRange(extra, second);
        await context.SaveChangesAsync();
        var at = new DateTime(2026, 10, 29, 0, 0, 0, DateTimeKind.Utc);
        await context.Findings.ExecuteUpdateAsync(setters => setters.SetProperty(finding => finding.FirstObservedAt, at));
        var version = await context.DocumentVersions.SingleAsync();
        context.FindingEvidence.Add(new FindingEvidence { FindingId = extra.Id, DocumentVersionId = version.Id, EvidenceText = new string('q', 11000) });
        await context.SaveChangesAsync();
        using var client = scenario.Client();
        var path = $"/archives/{scenario.ArchiveId}/findings";
        var page = (await client.GetFromJsonAsync<Page<FindingSummary>>(path + "?type=claim&status=disputed&page=2&pageSize=1"))!;
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(new[] { extra.Id, second.Id }.Order().Last(), Assert.Single(page.Items).Id);
        Assert.DoesNotContain(page.Items, finding => finding.Id == original.Id);
        var detail = (await client.GetFromJsonAsync<FindingDetails>(path + $"/{extra.Id}"))!;
        Assert.Equal(10000, detail.Finding.Statement.Length);
        Assert.True(detail.Finding.StatementTruncated);
        var evidence = Assert.Single(detail.Evidence.Items);
        Assert.Equal(10000, evidence.Quote!.Length);
        Assert.True(evidence.QuoteTruncated);
    }

    [Fact]
    public async Task SourceDetailsDoNotGrantOtherGlobalVersionsOrUnlinkedDocuments()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        var run = await ResearchTestData.SubmitAsync(scenario);
        await CompleteAsync(scenario, run.Id);
        await using var context = scenario.Context();
        var accepted = await context.DocumentVersions.SingleAsync();
        context.DocumentVersions.Add(new DocumentVersion { DocumentId = accepted.DocumentId, ContentHash = ContentFingerprint.Compute("Different private version"), ExtractedText = "Different private version" });
        var unlinked = await TestData.CreateVersionAsync(context);
        await context.SaveChangesAsync();
        using var client = scenario.Client("viewer");
        var path = $"/archives/{scenario.ArchiveId}/sources";
        var source = (await client.GetFromJsonAsync<SourceDetails>(path + $"/{accepted.DocumentId}"))!;
        Assert.Equal(1, source.VersionCount);
        Assert.Equal("https://example.com/source", source.Source.CanonicalUrl);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path + $"/{unlinked.DocumentId}")).StatusCode);
        var discovered = new Document { CanonicalUrl = "https://example.com/discovered" };
        context.Documents.Add(discovered);
        await context.SaveChangesAsync();
        context.ArchiveDocuments.Add(new ArchiveDocument { ArchiveId = scenario.ArchiveId, DocumentId = discovered.Id });
        await context.SaveChangesAsync();
        Assert.Equal(0, (await client.GetFromJsonAsync<SourceDetails>(path + $"/{discovered.Id}"))!.VersionCount);
    }

    [Theory]
    [InlineData("research-jobs")]
    [InlineData("findings")]
    [InlineData("findings/11111111-1111-1111-1111-111111111111")]
    [InlineData("sources/11111111-1111-1111-1111-111111111111")]
    public async Task ResultRoutesRequireActiveUserAndMembership(string route)
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        var path = $"/archives/{scenario.ArchiveId}/{route}";
        using var anonymous = scenario.Client(null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
        using var disabled = scenario.Client("disabled");
        Assert.Equal(HttpStatusCode.Unauthorized, (await disabled.GetAsync(path)).StatusCode);
        using var outsider = scenario.Client("nonmember");
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync(path)).StatusCode);
    }

    [Theory]
    [InlineData("research-jobs?page=0")]
    [InlineData("research-jobs?pageSize=101")]
    [InlineData("research-jobs?status=pending")]
    [InlineData("findings?status=completed")]
    [InlineData("findings?type=anything")]
    [InlineData("findings?pageSize=101")]
    public async Task InvalidFiltersAreRejected(string query)
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        using var client = scenario.Client();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/archives/{scenario.ArchiveId}/{query}")).StatusCode);
    }

    private static async Task CompleteAsync(ArchiveApiScenario scenario, Guid runId)
    {
        await using var scope = scenario.Scope();
        var queue = ResearchTestData.Queue(scope);
        Assert.True(await queue.CompleteAsync((await queue.ClaimAsync("results-test", default))!, ResearchTestData.Output(runId), default));
    }
}
