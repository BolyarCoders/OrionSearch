using System.Text.Json;
using OrionSearch.Application.Research;

namespace OrionSearch.Tests.Research;

public sealed class ResearchOutputTests
{
    [Fact]
    public async Task StableFixturesUseTheSameVersionAndPassSemanticValidation()
    {
        var request = JsonSerializer.Deserialize<AgentRequest>(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Contracts", "request.json")), ResearchContract.Json)!;
        var result = JsonSerializer.Deserialize<AgentResult>(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Contracts", "result.json")), ResearchContract.Json)!;
        Assert.Equal(ResearchContract.Version, request.ContractVersion);
        Assert.Equal(request.ResearchRunId, result.ResearchRunId);
        Assert.Equal(2, ResearchOutputValidator.Normalize(result, request.ResearchRunId).Sources.Length);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("run")]
    [InlineData("source-reference")]
    [InlineData("chunk-reference")]
    [InlineData("quote")]
    [InlineData("duplicate-chunk")]
    [InlineData("offset")]
    [InlineData("blank-text")]
    [InlineData("empty-capture")]
    [InlineData("raw-hash")]
    [InlineData("raw-storage")]
    [InlineData("publication-zone")]
    [InlineData("publisher")]
    [InlineData("canonical-duplicate")]
    [InlineData("finding-type")]
    [InlineData("confidence")]
    [InlineData("duplicate-query")]
    [InlineData("blank-report")]
    [InlineData("oversized-report")]
    public void InvalidOutputIsRejectedBeforePersistence(string problem)
    {
        var runId = Guid.NewGuid();
        var output = ResearchTestData.Output(runId);
        var source = output.Sources[0];
        var capture = source.Capture!;
        var finding = output.Findings[0];
        var evidence = finding.Evidence[0];
        output = problem switch
        {
            "version" => output with { ContractVersion = "unagreed-v9" },
            "run" => output with { ResearchRunId = Guid.NewGuid() },
            "source-reference" => output with { Findings = [finding with { Evidence = [evidence with { SourceKey = "another-archive-source" }] }] },
            "chunk-reference" => output with { Findings = [finding with { Evidence = [evidence with { ChunkIndex = 10 }] }] },
            "quote" => output with { Findings = [finding with { Evidence = [evidence with { Quote = "invented quotation" }] }] },
            "duplicate-chunk" => output with { Sources = [source with { Capture = capture with { Chunks = [capture.Chunks![0], capture.Chunks[0]] } }] },
            "offset" => output with { Sources = [source with { Capture = capture with { Chunks = [capture.Chunks![0] with { EndOffset = 999 }] } }] },
            "blank-text" => output with { Sources = [source with { Capture = capture with { ExtractedText = " \t\n" } }] },
            "empty-capture" => output with { Sources = [source with { Capture = capture with { ExtractedText = null, Raw = null } }] },
            "raw-hash" => output with { Sources = [source with { Capture = capture with { Raw = new RawCaptureOutput("urn:stored", "fake-hash") } }] },
            "raw-storage" => output with { Sources = [source with { Capture = capture with { Raw = new RawCaptureOutput(" ", new string('a', 64)) } }] },
            "publication-zone" => output with { Sources = [source with { PublishedAt = new DateTime(2026, 10, 18) }] },
            "publisher" => output with { Sources = [source with { Publisher = new PublisherOutput("Example", "https://example.com") }] },
            "canonical-duplicate" => output with { Sources = [source, source with { Key = "another", CanonicalUrl = "https://example.com/source" }] },
            "finding-type" => output with { Findings = [finding with { Type = "verified-truth" }] },
            "confidence" => output with { Findings = [finding with { Confidence = 1.5m }] },
            "duplicate-query" => output with { Queries = [output.Queries[0], output.Queries[0]] },
            "blank-report" => output with { Report = " \t\n" },
            "oversized-report" => output with { Report = new string('x', 100001) },
            _ => throw new InvalidOperationException()
        };
        Assert.Equal(ResearchFailure.Contract, Assert.Throws<ResearchAgentException>(() => ResearchOutputValidator.Normalize(output, runId)).Failure);
    }

    [Fact]
    public void ChunkOffsetsCountUnicodeScalarsAfterTextNormalization()
    {
        var runId = Guid.NewGuid();
        var output = ResearchTestData.Output(runId);
        output = output with
        {
            Sources = [output.Sources[0] with { Capture = new CaptureOutput(DateTime.UtcNow, " A😀 cafe\u0301.\r\n", Chunks: [new ChunkOutput(0, "A😀 café.", 0, 8)]) }],
            Findings = []
        };
        var normalized = ResearchOutputValidator.Normalize(output, runId);
        Assert.Equal("A😀 café.", normalized.Sources[0].Capture!.ExtractedText);
        Assert.Equal(8, normalized.Sources[0].Capture!.Chunks![0].EndOffset);
    }

    [Fact]
    public void RetryBackoffIsExponentialAndCapped()
    {
        var options = new ResearchWorkerOptions { RetryBaseSeconds = 10, RetryMaxSeconds = 60 };
        Assert.Equal(new[] { 10, 20, 40, 60, 60 }, Enumerable.Range(1, 5).Select(options.BackoffSeconds));
        Assert.Equal(60, options.BackoffSeconds(int.MaxValue));
    }
}
