using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrionSearch.Domain.Content;
using OrionSearch.Domain.Knowledge;

namespace OrionSearch.Tests.Persistence;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class RawCaptureTests(PostgreSqlFixture database)
{
    private const string RawHashMetadata = "{\"content_hash\":{\"algorithm\":\"sha256\",\"basis\":\"raw_bytes\"}}";

    [Fact]
    public async Task RawOnlyCaptureCanBeStoredAndCitedWithoutExtractedText()
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var extractedVersion = await TestData.CreateVersionAsync(context);
        var rawHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("<html>Captured source</html>")));
        var rawVersionId = Guid.NewGuid();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO content.document_versions (id, document_id, content_hash, extracted_text, storage_uri, metadata)
            VALUES ({rawVersionId}, {extractedVersion.DocumentId}, {rawHash}, NULL, 'urn:orion:test:captured-source', {RawHashMetadata}::jsonb)
            """);

        var rawVersion = await context.DocumentVersions.SingleAsync(x => x.Id == rawVersionId);
        var finding = new Finding { Statement = "A finding citing captured source material" };
        context.Findings.Add(finding);
        await context.SaveChangesAsync();
        context.FindingEvidence.Add(new FindingEvidence { FindingId = finding.Id, DocumentVersionId = rawVersion.Id });
        await context.SaveChangesAsync();

        Assert.Null(rawVersion.ExtractedText);
        Assert.Equal("urn:orion:test:captured-source", rawVersion.StorageUri);
        Assert.Equal(rawHash, rawVersion.ContentHash);
        using var metadata = JsonDocument.Parse(rawVersion.Metadata);
        Assert.Equal("raw_bytes", metadata.RootElement.GetProperty("content_hash").GetProperty("basis").GetString());
        Assert.Equal(rawVersion.Id, (await context.FindingEvidence.SingleAsync()).DocumentVersionId);
    }

    [Fact]
    public async Task PostgreSqlRejectsVersionsWithoutAnyCapturedRepresentation()
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var version = await TestData.CreateVersionAsync(context);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO content.document_versions (document_id, content_hash, extracted_text, storage_uri)
            VALUES ({version.DocumentId}, {ContentFingerprint.Compute("Another representation")}, NULL, NULL)
            """));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal("ck_document_versions_content_presence", exception.ConstraintName);
    }

    [Fact]
    public async Task EfCanPersistDistinctRawVersionsAndMatchingEvidence()
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var document = new Document { CanonicalUrl = "https://example.com/raw-only" };
        context.Documents.Add(document);
        await context.SaveChangesAsync();
        var first = NewRawVersion(document.Id, "<html>First capture</html>"u8.ToArray());
        var second = NewRawVersion(document.Id, "<html>Changed capture</html>"u8.ToArray());
        context.DocumentVersions.AddRange(first, second);
        await context.SaveChangesAsync();
        var chunk = await TestData.CreateChunkAsync(context, first.Id);
        var finding = new Finding { Statement = "A statement with a version-bound source" };
        context.Findings.Add(finding);
        await context.SaveChangesAsync();
        context.FindingEvidence.Add(new FindingEvidence
        {
            FindingId = finding.Id,
            DocumentVersionId = first.Id,
            ChunkId = chunk.Id,
            Stance = "mentions"
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var versions = await context.DocumentVersions.Where(x => x.DocumentId == document.Id).ToListAsync();
        Assert.Equal(2, versions.Count);
        Assert.All(versions, version => Assert.Null(version.ExtractedText));
        Assert.NotEqual(first.ContentHash, second.ContentHash);
        Assert.Equal(first.Id, (await context.FindingEvidence.SingleAsync()).DocumentVersionId);
    }

    [Theory]
    [InlineData("not-a-sha256")]
    [InlineData("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD")]
    public async Task RawOnlyVersionsRequireValidHashes(string hash)
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var document = new Document { CanonicalUrl = "https://example.com/raw-hash" };
        context.Documents.Add(document);
        await context.SaveChangesAsync();
        var version = NewRawVersion(document.Id, "actual bytes"u8.ToArray());
        version.ContentHash = hash;
        context.DocumentVersions.Add(version);

        await TestData.AssertRejectedAsync(() => context.SaveChangesAsync(), PostgresErrorCodes.CheckViolation, "ck_document_versions_hash");
    }

    [Fact]
    public async Task RawOnlyVersionsCannotOmitTheHash()
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var version = await TestData.CreateVersionAsync(context);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO content.document_versions (document_id, extracted_text, storage_uri, metadata)
            VALUES ({version.DocumentId}, NULL, 'urn:orion:test:raw', {RawHashMetadata}::jsonb)
            """));

        Assert.Equal(PostgresErrorCodes.NotNullViolation, exception.SqlState);
        Assert.Equal("content_hash", exception.ColumnName);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"content_hash\":{\"algorithm\":\"sha256\"}}")]
    [InlineData("{\"content_hash\":{\"algorithm\":\"md5\",\"basis\":\"raw_bytes\"}}")]
    [InlineData("{\"content_hash\":{\"algorithm\":\"sha256\",\"basis\":\"storage_uri\"}}")]
    public async Task RawOnlyVersionsRequireHashProvenance(string metadata)
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var document = new Document { CanonicalUrl = "https://example.com/raw-provenance" };
        context.Documents.Add(document);
        await context.SaveChangesAsync();
        var version = NewRawVersion(document.Id, "actual bytes"u8.ToArray());
        version.Metadata = metadata;
        context.DocumentVersions.Add(version);

        await TestData.AssertRejectedAsync(() => context.SaveChangesAsync(), PostgresErrorCodes.CheckViolation, "ck_document_versions_raw_hash_metadata");
    }

    [Fact]
    public async Task DuplicateRawContentIsRejectedForTheSameDocument()
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var document = new Document { CanonicalUrl = "https://example.com/raw-duplicate" };
        context.Documents.Add(document);
        await context.SaveChangesAsync();
        context.DocumentVersions.Add(NewRawVersion(document.Id, "same bytes"u8.ToArray()));
        await context.SaveChangesAsync();
        context.DocumentVersions.Add(NewRawVersion(document.Id, "same bytes"u8.ToArray()));

        await TestData.AssertRejectedAsync(() => context.SaveChangesAsync(), PostgresErrorCodes.UniqueViolation, "ux_document_versions_content");
    }

    [Fact]
    public async Task RawOnlyVersionCannotCiteAnotherVersionsChunk()
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var extractedVersion = await TestData.CreateVersionAsync(context);
        var chunk = await TestData.CreateChunkAsync(context, extractedVersion.Id);
        var rawVersion = NewRawVersion(extractedVersion.DocumentId, "raw bytes"u8.ToArray());
        var finding = new Finding { Statement = "A finding" };
        context.DocumentVersions.Add(rawVersion);
        context.Findings.Add(finding);
        await context.SaveChangesAsync();
        context.FindingEvidence.Add(new FindingEvidence
        {
            FindingId = finding.Id,
            DocumentVersionId = rawVersion.Id,
            ChunkId = chunk.Id
        });

        await TestData.AssertRejectedAsync(() => context.SaveChangesAsync(), PostgresErrorCodes.ForeignKeyViolation, "fk_finding_evidence_chunk_version");
    }

    private static DocumentVersion NewRawVersion(Guid documentId, byte[] content) => new()
    {
        DocumentId = documentId,
        ContentHash = ContentFingerprint.ComputeRaw(content),
        StorageUri = "urn:orion:test:durable-object",
        Metadata = RawHashMetadata
    };
}
