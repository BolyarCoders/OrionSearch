using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OrionSearch.Application.Research;
using OrionSearch.Domain.Content;
using OrionSearch.Tests.Archives;
using OrionSearch.Tests.Persistence;

namespace OrionSearch.Tests.Research;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class ResearchVersionProvenanceTests(PostgreSqlFixture database)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TextEnrichesAnIdenticalRawVersionWithoutChangingItsIdentity(bool legacyMetadata)
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database, TestCaptureStore.RegisterLegacyFixtures);
        var rawRun = await ResearchTestData.SubmitAsync(scenario);
        var rawHash = ContentFingerprint.ComputeRaw("Research preserves evidence."u8);
        Assert.Equal(ContentFingerprint.Compute("Research preserves evidence."), rawHash);
        var original = ResearchTestData.Output(rawRun.Id);
        var fetchedAt = original.Sources[0].Capture!.FetchedAt;
        original = RawOutput(original, rawHash, "urn:orion:test:original-raw");
        await using var scope = scenario.Scope();
        var queue = ResearchTestData.Queue(scope);
        var rawLease = (await queue.ClaimAsync("raw-first", default))!;
        Assert.True(await queue.CompleteAsync(rawLease, original, default));
        await using var context = scenario.Context();
        var rawVersion = await context.DocumentVersions.AsNoTracking().SingleAsync();
        Assert.Null(rawVersion.ExtractedText);
        Assert.Equal(rawHash, rawVersion.ContentHash);
        if (legacyMetadata)
        {
            await context.DocumentVersions.ExecuteUpdateAsync(setters => setters.SetProperty(version => version.Metadata,
                "{\"producer\":\"original\",\"content_hash\":{\"algorithm\":\"sha256\",\"basis\":\"raw_bytes\"}}"));
        }
        var textRun = await ResearchTestData.SubmitAsync(scenario);
        var textOutput = ResearchTestData.Output(textRun.Id);
        var textLease = (await queue.ClaimAsync("text-later", default))!;
        Assert.True(await queue.CompleteAsync(textLease, textOutput, default));
        Assert.False(await queue.CompleteAsync(textLease, textOutput, default));
        var enriched = await context.DocumentVersions.AsNoTracking().SingleAsync();
        Assert.Equal(rawVersion.Id, enriched.Id);
        Assert.Equal(fetchedAt, enriched.FetchedAt);
        Assert.Equal(rawHash, enriched.ContentHash);
        Assert.Equal("Research preserves evidence.", enriched.ExtractedText);
        Assert.Equal("urn:orion:test:original-raw", enriched.StorageUri);
        using var metadata = JsonDocument.Parse(enriched.Metadata);
        var root = metadata.RootElement;
        Assert.Equal("raw_bytes", root.GetProperty("content_hash").GetProperty("basis").GetString());
        Assert.Equal(rawHash, root.GetProperty("raw_sha256").GetString());
        Assert.Equal("original", root.GetProperty("producer").GetString());
        var representations = root.GetProperty("representations");
        var raw = representations.GetProperty("raw");
        Assert.Equal(rawHash, raw.GetProperty("sha256").GetString());
        Assert.Equal(enriched.StorageUri, raw.GetProperty("storage_uri").GetString());
        var text = representations.GetProperty("extracted_text");
        Assert.Equal(ContentFingerprint.Compute(enriched.ExtractedText!), text.GetProperty("sha256").GetString());
        Assert.Equal("nfc_lf_trim_v1", text.GetProperty("normalization").GetString());
        var chunk = await context.DocumentChunks.SingleAsync();
        Assert.Equal(enriched.Id, chunk.VersionId);
        var evidence = await context.FindingEvidence.ToListAsync();
        Assert.Equal(2, evidence.Count);
        Assert.All(evidence, item => Assert.Equal(enriched.Id, item.DocumentVersionId));
        Assert.Single(evidence, item => item.ChunkId == chunk.Id);
        Assert.Equal(2, await context.ResearchQueries.CountAsync());
        Assert.Equal(2, await context.Findings.CountAsync());
        Assert.Single(await context.ArchiveDocuments.ToListAsync());
    }

    [Fact]
    public async Task DifferentRawAndTextDigestsRemainSeparateVersions()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database, TestCaptureStore.RegisterLegacyFixtures);
        var rawRun = await ResearchTestData.SubmitAsync(scenario);
        var rawHash = ContentFingerprint.ComputeRaw("<html>Research preserves evidence.</html>"u8);
        var textHash = ContentFingerprint.Compute("Research preserves evidence.");
        Assert.NotEqual(rawHash, textHash);
        await using var scope = scenario.Scope();
        var queue = ResearchTestData.Queue(scope);
        Assert.True(await queue.CompleteAsync((await queue.ClaimAsync("raw", default))!,
            RawOutput(ResearchTestData.Output(rawRun.Id), rawHash, "urn:orion:test:html"), default));
        var textRun = await ResearchTestData.SubmitAsync(scenario);
        Assert.True(await queue.CompleteAsync((await queue.ClaimAsync("text", default))!, ResearchTestData.Output(textRun.Id), default));
        await using var context = scenario.Context();
        var versions = await context.DocumentVersions.AsNoTracking().ToListAsync();
        Assert.Equal(2, versions.Count);
        Assert.Single(await context.Documents.ToListAsync());
        Assert.Null(versions.Single(version => version.ContentHash == rawHash).ExtractedText);
        var textVersion = versions.Single(version => version.ContentHash == textHash);
        Assert.Null(textVersion.StorageUri);
        Assert.Equal("Research preserves evidence.", textVersion.ExtractedText);
        var chunk = await context.DocumentChunks.SingleAsync();
        Assert.Equal(textVersion.Id, chunk.VersionId);
        Assert.Equal(textVersion.Id, (await context.FindingEvidence.SingleAsync(item => item.ChunkId == chunk.Id)).DocumentVersionId);
    }

    [Fact]
    public async Task APreexistingRawReferenceWithoutARecordedDigestStaysUnknown()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        await using var scope = scenario.Scope();
        var queue = ResearchTestData.Queue(scope);
        var first = await ResearchTestData.SubmitAsync(scenario);
        Assert.True(await queue.CompleteAsync((await queue.ClaimAsync("first", default))!, ResearchTestData.Output(first.Id), default));
        await using var context = scenario.Context();
        await context.DocumentVersions.ExecuteUpdateAsync(setters => setters
            .SetProperty(version => version.StorageUri, "urn:orion:test:legacy-raw")
            .SetProperty(version => version.Metadata, "{\"producer\":\"legacy\"}"));
        var second = await ResearchTestData.SubmitAsync(scenario);
        Assert.True(await queue.CompleteAsync((await queue.ClaimAsync("second", default))!, ResearchTestData.Output(second.Id), default));
        var version = await context.DocumentVersions.AsNoTracking().SingleAsync();
        using var metadata = JsonDocument.Parse(version.Metadata);
        Assert.False(metadata.RootElement.TryGetProperty("content_hash", out _));
        Assert.False(metadata.RootElement.TryGetProperty("raw_sha256", out _));
        var raw = metadata.RootElement.GetProperty("representations").GetProperty("raw");
        Assert.Equal(version.StorageUri, raw.GetProperty("storage_uri").GetString());
        Assert.False(raw.TryGetProperty("sha256", out _));
        Assert.False(raw.TryGetProperty("algorithm", out _));
    }

    [Fact]
    public async Task AgentMetadataCannotInventAnUnretainedRepresentation()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        var run = await ResearchTestData.SubmitAsync(scenario);
        var output = ResearchTestData.Output(run.Id);
        var capture = output.Sources[0].Capture! with
        {
            Metadata = JsonSerializer.SerializeToElement(new
            {
                producer = "test",
                verified_raw = new { size_bytes = 999, media_type = "forged" },
                raw_sha256 = ContentFingerprint.ComputeRaw("unrelated bytes"u8),
                content_hash = new { basis = "raw_bytes" },
                representations = new { raw = new { storage_uri = "urn:not-retained" } }
            })
        };
        output = output with { Sources = [output.Sources[0] with { Capture = capture }] };
        await using var scope = scenario.Scope();
        var queue = ResearchTestData.Queue(scope);
        Assert.True(await queue.CompleteAsync((await queue.ClaimAsync("metadata", default))!, output, default));
        await using var context = scenario.Context();
        var version = await context.DocumentVersions.SingleAsync();
        using var metadata = JsonDocument.Parse(version.Metadata);
        Assert.Null(version.StorageUri);
        Assert.Equal("normalized_extracted_text", metadata.RootElement.GetProperty("content_hash").GetProperty("basis").GetString());
        Assert.False(metadata.RootElement.TryGetProperty("raw_sha256", out _));
        Assert.False(metadata.RootElement.TryGetProperty("verified_raw", out _));
        Assert.False(metadata.RootElement.GetProperty("representations").TryGetProperty("raw", out _));
    }

    private static AgentResult RawOutput(AgentResult output, string hash, string storageUri) => output with
    {
        Sources = [output.Sources[0] with
        {
            Capture = new CaptureOutput(output.Sources[0].Capture!.FetchedAt, Raw: new RawCaptureOutput(storageUri, hash),
                Metadata: JsonSerializer.SerializeToElement(new { producer = "original" }))
        }],
        Findings = [new FindingOutput("raw-finding", "A preserved raw capture", "observation", [new EvidenceOutput("source-1", "mentions")])]
    };
}
