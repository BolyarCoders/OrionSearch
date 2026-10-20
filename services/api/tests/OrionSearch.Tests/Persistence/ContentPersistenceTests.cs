using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrionSearch.Domain.Content;

namespace OrionSearch.Tests.Persistence;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class ContentPersistenceTests(PostgreSqlFixture database)
{
    [Fact]
    public async Task NormalizedCanonicalUrlsAreUnique()
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        context.Documents.Add(new Document { CanonicalUrl = "HTTPS://EXAMPLE.COM:443/article#section" });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        Assert.Equal("https://example.com/article", (await context.Documents.SingleAsync()).CanonicalUrl);
        context.Documents.Add(new Document { CanonicalUrl = "https://example.com/article" });

        await TestData.AssertRejectedAsync(() => context.SaveChangesAsync(), PostgresErrorCodes.UniqueViolation, "ux_documents_canonical_url");
    }

    [Fact]
    public async Task NormalizedPublisherDomainsAreUnique()
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        context.Publishers.Add(new Publisher { Name = "Source", Domain = " EXAMPLE.COM. " });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        Assert.Equal("example.com", (await context.Publishers.SingleAsync()).Domain);
        context.Publishers.Add(new Publisher { Name = "Duplicate", Domain = "example.com" });

        await TestData.AssertRejectedAsync(() => context.SaveChangesAsync(), PostgresErrorCodes.UniqueViolation, "ux_publishers_domain");
    }

    [Fact]
    public async Task IdenticalVersionsOfOneDocumentAreRejected()
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var version = await TestData.CreateVersionAsync(context);
        context.DocumentVersions.Add(new DocumentVersion
        {
            DocumentId = version.DocumentId,
            ContentHash = version.ContentHash,
            ExtractedText = version.ExtractedText
        });

        await TestData.AssertRejectedAsync(() => context.SaveChangesAsync(), PostgresErrorCodes.UniqueViolation, "ux_document_versions_content");
    }

    [Fact]
    public async Task ChangedContentCreatesAnotherVersionAndPublicationCanRemainUnknown()
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var version = await TestData.CreateVersionAsync(context);
        context.DocumentVersions.Add(new DocumentVersion
        {
            DocumentId = version.DocumentId,
            ContentHash = ContentFingerprint.Compute("Changed content"),
            ExtractedText = "Changed content",
            Metadata = "{\"extractor\":\"v1\",\"pages\":2}"
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var versions = await context.DocumentVersions.Where(x => x.DocumentId == version.DocumentId).ToListAsync();
        var document = await context.Documents.SingleAsync(x => x.Id == version.DocumentId);
        using var metadata = JsonDocument.Parse(versions.Single(x => x.ContentHash != version.ContentHash).Metadata);
        Assert.Equal(2, versions.Count);
        Assert.Null(document.PublishedAt);
        Assert.Equal(2, metadata.RootElement.GetProperty("pages").GetInt32());
        Assert.All(versions, x => Assert.Equal(DateTimeKind.Utc, x.FetchedAt.Kind));
        Assert.Equal(DateTimeKind.Utc, document.FirstSeenAt.Kind);
        Assert.Equal(DateTimeKind.Utc, document.LastSeenAt.Kind);
    }

    [Fact]
    public async Task OneGlobalDocumentCanBelongToMultipleArchives()
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var firstArchive = await TestData.CreateArchiveAsync(context);
        var secondArchive = await TestData.CreateArchiveAsync(context);
        var version = await TestData.CreateVersionAsync(context);
        context.ArchiveDocuments.AddRange(
            new ArchiveDocument { ArchiveId = firstArchive.Id, DocumentId = version.DocumentId, RelevanceScore = 0.8m },
            new ArchiveDocument { ArchiveId = secondArchive.Id, DocumentId = version.DocumentId });
        await context.SaveChangesAsync();

        Assert.Equal(1, await context.Documents.CountAsync());
        Assert.Equal(2, await context.ArchiveDocuments.CountAsync(x => x.DocumentId == version.DocumentId));

        await context.Archives.Where(x => x.Id == firstArchive.Id).ExecuteDeleteAsync();

        Assert.Equal(1, await context.Documents.CountAsync());
        Assert.Equal(secondArchive.Id, (await context.ArchiveDocuments.SingleAsync()).ArchiveId);
    }

    [Theory]
    [InlineData("not-a-sha256")]
    [InlineData("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD")]
    public async Task InvalidVersionHashesAreRejected(string hash)
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var version = await TestData.CreateVersionAsync(context);
        version.ContentHash = hash;

        await TestData.AssertRejectedAsync(() => context.SaveChangesAsync(), PostgresErrorCodes.CheckViolation, "ck_document_versions_hash");
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public async Task InvalidRelevanceScoresAreRejected(double score)
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var archive = await TestData.CreateArchiveAsync(context);
        var version = await TestData.CreateVersionAsync(context);
        context.ArchiveDocuments.Add(new ArchiveDocument
        {
            ArchiveId = archive.Id,
            DocumentId = version.DocumentId,
            RelevanceScore = (decimal)score
        });

        await TestData.AssertRejectedAsync(() => context.SaveChangesAsync(), PostgresErrorCodes.CheckViolation, "ck_archive_documents_relevance");
    }

    [Theory]
    [InlineData(-1, null, null, null, "ck_document_chunks_index")]
    [InlineData(0, -1, null, null, "ck_document_chunks_tokens")]
    [InlineData(0, null, -1, 1, "ck_document_chunks_offsets")]
    [InlineData(0, null, 2, 1, "ck_document_chunks_offsets")]
    [InlineData(0, null, 1, 1, "ck_document_chunks_offsets")]
    [InlineData(0, null, null, 1, "ck_document_chunks_offsets")]
    [InlineData(0, null, 0, null, "ck_document_chunks_offsets")]
    public async Task InvalidChunkBoundsAreRejected(int index, int? tokens, int? start, int? end, string constraint)
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var version = await TestData.CreateVersionAsync(context);
        context.DocumentChunks.Add(new DocumentChunk
        {
            VersionId = version.Id,
            ChunkIndex = index,
            TokenCount = tokens,
            StartOffset = start,
            EndOffset = end,
            Content = "Evidence"
        });

        await TestData.AssertRejectedAsync(() => context.SaveChangesAsync(), PostgresErrorCodes.CheckViolation, constraint);
    }

    [Fact]
    public async Task DuplicateChunkIndexesWithinAVersionAreRejected()
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var version = await TestData.CreateVersionAsync(context);
        await TestData.CreateChunkAsync(context, version.Id);
        context.DocumentChunks.Add(new DocumentChunk { VersionId = version.Id, ChunkIndex = 0, Content = "Another chunk" });

        await TestData.AssertRejectedAsync(() => context.SaveChangesAsync(), PostgresErrorCodes.UniqueViolation, "ux_document_chunks_version_index");
    }

    [Fact]
    public async Task SimpleFullTextSearchFindsChunkContent()
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var version = await TestData.CreateVersionAsync(context);
        var chunk = await TestData.CreateChunkAsync(context, version.Id);

        var matches = await context.DocumentChunks.Where(x =>
            EF.Functions.ToTsVector("simple", x.Content).Matches(EF.Functions.PlainToTsQuery("simple", "evidence"))).ToListAsync();

        Assert.Equal(chunk.Id, Assert.Single(matches).Id);
    }

    [Fact]
    public async Task NonUtcTimestampsAreRejectedByTheProvider()
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        context.Documents.Add(new Document
        {
            CanonicalUrl = "https://example.com/non-utc",
            PublishedAt = new DateTime(2026, 10, 16, 0, 0, 0, DateTimeKind.Unspecified)
        });

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        Assert.IsType<ArgumentException>(exception.InnerException);
    }
}
