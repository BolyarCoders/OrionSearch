using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using OrionSearch.Application.Research;
using OrionSearch.Domain.Content;
using OrionSearch.Domain.Knowledge;
using OrionSearch.Domain.Research;
using OrionSearch.Infrastructure.Persistence;

namespace OrionSearch.Infrastructure.Research;

internal static class ResearchIngestion
{
    public static async Task ApplyAsync(OrionSearchDbContext context, Guid runId, Guid archiveId, AgentResult result, CancellationToken ct)
    {
        var publishers = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var publisher in result.Sources.Where(source => source.Publisher is not null).Select(source => source.Publisher!)
            .DistinctBy(publisher => publisher.Domain).OrderBy(publisher => publisher.Domain, StringComparer.Ordinal))
        {
            publishers[publisher.Domain] = await IdAsync(context, $"""
                INSERT INTO content.publishers (name, domain) VALUES ({publisher.Name}, {publisher.Domain})
                ON CONFLICT (domain) DO UPDATE SET domain = EXCLUDED.domain RETURNING id AS "Value"
                """, ct);
        }
        var references = new Dictionary<string, SourceReference>(StringComparer.Ordinal);
        foreach (var source in result.Sources.OrderBy(source => source.CanonicalUrl, StringComparer.Ordinal))
        {
            Guid? publisherId = source.Publisher is null ? null : publishers[source.Publisher.Domain];
            var documentId = await IdAsync(context, $"""
                INSERT INTO content.documents (publisher_id, canonical_url, title, language, published_at)
                VALUES ({publisherId}, {source.CanonicalUrl}, {source.Title}, {source.Language}, {source.PublishedAt})
                ON CONFLICT (canonical_url) DO UPDATE SET
                    last_seen_at = GREATEST(content.documents.last_seen_at, clock_timestamp()),
                    title = COALESCE(content.documents.title, EXCLUDED.title),
                    language = COALESCE(content.documents.language, EXCLUDED.language),
                    publisher_id = COALESCE(content.documents.publisher_id, EXCLUDED.publisher_id),
                    published_at = COALESCE(content.documents.published_at, EXCLUDED.published_at)
                RETURNING id AS "Value"
                """, ct);
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO content.archive_documents (archive_id, document_id, relevance_score)
                VALUES ({archiveId}, {documentId}, {source.Relevance})
                ON CONFLICT (archive_id, document_id) DO UPDATE SET
                    relevance_score = COALESCE(EXCLUDED.relevance_score, content.archive_documents.relevance_score)
                """, ct);
            var reference = new SourceReference();
            references[source.Key] = reference;
            if (source.Capture is not { } capture) continue;
            var hash = capture.ExtractedText is { } text ? ContentFingerprint.Compute(text) : capture.Raw!.Sha256;
            var metadata = capture.Metadata.ValueKind == JsonValueKind.Undefined ? new JsonObject() : JsonNode.Parse(capture.Metadata.GetRawText())!.AsObject();
            metadata.Remove("representations");
            metadata.Remove("raw_sha256");
            metadata["content_hash"] = capture.ExtractedText is null
                ? JsonSerializer.SerializeToNode(new { algorithm = "sha256", basis = "raw_bytes" })
                : JsonSerializer.SerializeToNode(new { algorithm = "sha256", basis = "normalized_extracted_text", normalization = "nfc_lf_trim_v1" });
            if (capture.Raw is { } raw) metadata["raw_sha256"] = raw.Sha256;
            var metadataJson = metadata.ToJsonString();
            var storageUri = capture.Raw?.StorageUri;
            var versionId = await IdAsync(context, $"""
                INSERT INTO content.document_versions (document_id, content_hash, extracted_text, storage_uri, fetched_at, metadata)
                VALUES ({documentId}, {hash}, {capture.ExtractedText}, {storageUri}, {capture.FetchedAt}, {metadataJson}::jsonb)
                ON CONFLICT (document_id, content_hash) DO UPDATE SET
                    extracted_text = COALESCE(content.document_versions.extracted_text, EXCLUDED.extracted_text),
                    storage_uri = COALESCE(content.document_versions.storage_uri, EXCLUDED.storage_uri),
                    metadata = CASE WHEN content.document_versions.storage_uri IS NULL AND EXCLUDED.storage_uri IS NOT NULL
                        THEN content.document_versions.metadata || jsonb_build_object('raw_sha256', EXCLUDED.metadata->'raw_sha256', 'verified_raw', EXCLUDED.metadata->'verified_raw')
                        ELSE content.document_versions.metadata END
                RETURNING id AS "Value"
                """, ct);
            var version = await context.DocumentVersions.SingleAsync(version => version.Id == versionId, ct);
            version.Metadata = RepresentationMetadata(version);
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO content.archive_document_versions (archive_id, document_id, document_version_id, storage_uri, raw_sha256)
                VALUES ({archiveId}, {documentId}, {versionId}, {storageUri}, {(capture.Raw == null ? null : capture.Raw.Sha256)})
                ON CONFLICT (archive_id, document_version_id) DO UPDATE SET
                    storage_uri = COALESCE(content.archive_document_versions.storage_uri, EXCLUDED.storage_uri),
                    raw_sha256 = COALESCE(content.archive_document_versions.raw_sha256, EXCLUDED.raw_sha256)
                """, ct);
            reference.VersionId = versionId;
            foreach (var chunk in capture.Chunks!.OrderBy(chunk => chunk.Index))
            {
                var id = await IdAsync(context, $"""
                    INSERT INTO content.document_chunks (version_id, chunk_index, content, token_count, start_offset, end_offset)
                    VALUES ({versionId}, {chunk.Index}, {chunk.Text}, {chunk.TokenCount}, {chunk.StartOffset}, {chunk.EndOffset})
                    ON CONFLICT (version_id, chunk_index) DO UPDATE SET version_id = EXCLUDED.version_id
                    RETURNING id AS "Value"
                    """, ct);
                var stored = await context.DocumentChunks.AsNoTracking().SingleAsync(existing => existing.Id == id, ct);
                if (stored.Content != chunk.Text || stored.StartOffset != chunk.StartOffset || stored.EndOffset != chunk.EndOffset)
                    throw new ResearchAgentException(ResearchFailure.Contract);
                reference.Chunks.Add(chunk.Index, id);
            }
        }
        context.ResearchQueries.AddRange(result.Queries.Select(query => new ResearchQuery
        {
            Id = Guid.NewGuid(),
            RunId = runId,
            Query = query.Query,
            Provider = query.Provider,
            Status = "completed"
        }));
        foreach (var output in result.Findings)
        {
            var finding = new Finding
            {
                Id = Guid.NewGuid(),
                ResearchRunId = runId,
                Statement = output.Statement.Trim(),
                FindingType = output.Type,
                Confidence = output.Confidence,
                Status = "unverified"
            };
            context.Findings.Add(finding);
            foreach (var evidence in output.Evidence)
            {
                var source = references[evidence.SourceKey];
                context.FindingEvidence.Add(new FindingEvidence
                {
                    Id = Guid.NewGuid(),
                    FindingId = finding.Id,
                    DocumentVersionId = source.VersionId!.Value,
                    ChunkId = evidence.ChunkIndex is { } index ? source.Chunks[index] : null,
                    EvidenceText = evidence.Quote,
                    Stance = evidence.Stance
                });
            }
        }
    }

    private static string RepresentationMetadata(DocumentVersion version)
    {
        var metadata = JsonNode.Parse(version.Metadata)!.AsObject();
        var representations = new JsonObject();
        if (version.ExtractedText is { } text)
            representations["extracted_text"] = JsonSerializer.SerializeToNode(new
            {
                algorithm = "sha256",
                sha256 = ContentFingerprint.Compute(text),
                normalization = "nfc_lf_trim_v1"
            });
        if (version.StorageUri is { } storageUri)
        {
            var raw = new JsonObject { ["storage_uri"] = storageUri };
            var digest = String(metadata["raw_sha256"]);
            if (digest is null && metadata["content_hash"] is JsonObject identity
                && String(identity["algorithm"]) == "sha256" && String(identity["basis"]) == "raw_bytes")
                digest = version.ContentHash;
            if (digest is not null)
            {
                raw["algorithm"] = "sha256";
                raw["sha256"] = digest;
                metadata["raw_sha256"] = digest;
            }
            representations["raw"] = raw;
        }
        // Identity provenance stays unchanged; these describe the representations actually retained.
        metadata["representations"] = representations;
        return metadata.ToJsonString();
    }

    private static string? String(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static async Task<Guid> IdAsync(OrionSearchDbContext context, FormattableString sql, CancellationToken ct)
    {
        await foreach (var id in context.Database.SqlQuery<Guid>(sql).AsAsyncEnumerable().WithCancellation(ct)) return id;
        throw new InvalidOperationException("An ingestion statement returned no identity.");
    }

    private sealed class SourceReference
    {
        public Guid? VersionId { get; set; }
        public Dictionary<int, Guid> Chunks { get; } = [];
    }
}
