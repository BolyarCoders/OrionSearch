using System.Text;
using System.Text.Json;
using OrionSearch.Application.Archives;
using OrionSearch.Domain.Content;

namespace OrionSearch.Application.Research;

public static class ResearchOutputValidator
{
    public static AgentResult Normalize(AgentResult? result, Guid runId, string expectedVersion = ResearchContract.Version)
    {
        try
        {
            if (result is null) throw new ResearchAgentException(ResearchFailure.Contract);
            Require(ResearchContract.IsSupported(expectedVersion, allowProvisional: true)
                && result.ContractVersion == expectedVersion && result.ResearchRunId == runId);
            Require(result.Queries is { Length: <= 256 } && result.Sources is { Length: <= 256 } && result.Findings is { Length: <= 256 });
            var queries = result.Queries!.Select(query =>
            {
                Require(query is not null && Text(query.Query, 2000) && Text(query.Provider, 200));
                return query! with { Query = query.Query.Trim(), Provider = query.Provider.Trim().ToLowerInvariant() };
            }).ToArray();
            Require(queries.Distinct().Count() == queries.Length);
            var sources = result.Sources!.Select(NormalizeSource).ToArray();
            Require(sources.Select(source => source.Key).Distinct(StringComparer.Ordinal).Count() == sources.Length);
            Require(sources.Select(source => source.CanonicalUrl).Distinct(StringComparer.Ordinal).Count() == sources.Length);
            var byKey = sources.ToDictionary(source => source.Key, StringComparer.Ordinal);
            Require(result.Findings!.All(finding => finding is not null && Key(finding.Key)));
            Require(result.Findings!.Select(finding => finding.Key).Distinct(StringComparer.Ordinal).Count() == result.Findings.Length);
            foreach (var finding in result.Findings)
            {
                Require(Text(finding.Statement, 10000) && finding.Type is "claim" or "observation" or "estimate" && Score(finding.Confidence));
                Require(finding.Evidence is { Length: <= 100 });
                foreach (var evidence in finding.Evidence!)
                {
                    Require(evidence is not null && evidence.Stance is "supports" or "contradicts" or "mentions");
                    Require(evidence!.SourceKey is not null && byKey.ContainsKey(evidence.SourceKey));
                    var capture = byKey[evidence.SourceKey!].Capture;
                    Require(capture is not null);
                    var content = capture!.ExtractedText;
                    if (evidence.ChunkIndex is { } index)
                    {
                        var chunk = capture.Chunks!.SingleOrDefault(chunk => chunk.Index == index);
                        Require(chunk is not null);
                        content = chunk!.Text;
                    }
                    if (evidence.Quote is not null)
                        Require(Text(evidence.Quote, 10000) && content is not null && content.Contains(evidence.Quote, StringComparison.Ordinal));
                }
                Require(finding.Evidence.Distinct().Count() == finding.Evidence.Length);
            }
            return result with { Queries = queries, Sources = sources };
        }
        catch (Exception exception) when (exception is ArgumentException or ArchiveRequestException or JsonException)
        {
            throw new ResearchAgentException(ResearchFailure.Contract);
        }
    }

    private static SourceOutput NormalizeSource(SourceOutput source)
    {
        Require(source is not null && Key(source.Key) && Text(source.CanonicalUrl, 4096));
        Require(source!.Title is null || Text(source.Title, 2000));
        Require(source.PublishedAt is null || source.PublishedAt.Value.Kind == DateTimeKind.Utc);
        Require(Score(source.Relevance));
        var publisher = source.Publisher;
        if (publisher is not null)
        {
            Require(Text(publisher.Name, 200) && Text(publisher.Domain, 253));
            publisher = publisher with { Name = publisher.Name.Trim(), Domain = DomainName.Normalize(publisher.Domain) };
        }
        return source with
        {
            CanonicalUrl = CanonicalUrl.Normalize(source.CanonicalUrl),
            Language = source.Language is null ? null : ArchiveValidation.Language(source.Language),
            Publisher = publisher,
            Capture = source.Capture is null ? null : NormalizeCapture(source.Capture)
        };
    }

    private static CaptureOutput NormalizeCapture(CaptureOutput capture)
    {
        Require(capture.FetchedAt.Kind == DateTimeKind.Utc && capture.FetchedAt >= DateTime.UnixEpoch);
        Require(capture.ExtractedText is null || Text(capture.ExtractedText, 1000000));
        Require(capture.ExtractedText is not null || capture.Raw is not null);
        if (capture.Raw is { } raw)
        {
            Require(Text(raw.StorageUri, 4096) && Uri.TryCreate(raw.StorageUri, UriKind.Absolute, out var uri)
                && uri.Scheme is "https" or "s3" or "gs" or "urn" && uri.UserInfo.Length == 0);
            Require(raw.Sha256 is { Length: 64 } && raw.Sha256.All(character => char.IsAsciiHexDigit(character) && !char.IsAsciiLetterUpper(character)));
        }
        Require(capture.Metadata.ValueKind is JsonValueKind.Undefined or JsonValueKind.Object);
        Require(capture.Metadata.ValueKind == JsonValueKind.Undefined || Encoding.UTF8.GetByteCount(capture.Metadata.GetRawText()) <= 16384);
        var text = capture.ExtractedText is null ? null : ContentFingerprint.Normalize(capture.ExtractedText);
        var chunks = capture.Chunks ?? [];
        Require(chunks.Length <= 256 && chunks.All(chunk => chunk is not null && chunk.Index >= 0));
        Require(chunks.Select(chunk => chunk.Index).Distinct().Count() == chunks.Length);
        var runes = text?.EnumerateRunes().ToArray();
        foreach (var chunk in chunks)
        {
            Require(text is not null && Text(chunk.Text, 100000) && (chunk.TokenCount is null || chunk.TokenCount >= 0));
            Require((chunk.StartOffset is null) == (chunk.EndOffset is null));
            if (chunk.StartOffset is { } start && chunk.EndOffset is { } end)
            {
                Require(start >= 0 && end > start && end <= runes!.Length);
                Require(string.Concat(runes![start..end].Select(rune => rune.ToString())) == chunk.Text);
            }
            else Require(text!.Contains(chunk.Text, StringComparison.Ordinal));
        }
        return capture with { ExtractedText = text, Chunks = chunks };
    }

    private static bool Text(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value.Length <= maximum;
    private static bool Key(string? value) => Text(value, 80) && value!.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');
    private static bool Score(decimal? value) => value is null or >= 0 and <= 1;
    private static void Require(bool valid)
    {
        if (!valid) throw new ResearchAgentException(ResearchFailure.Contract);
    }
}
