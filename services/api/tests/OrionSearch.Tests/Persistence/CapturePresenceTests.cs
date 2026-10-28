using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrionSearch.Domain.Content;

namespace OrionSearch.Tests.Persistence;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class CapturePresenceTests(PostgreSqlFixture database)
{
    [Theory]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    [InlineData("\v")]
    [InlineData("\u00a0\u2003\u3000")]
    public async Task BlankTextCannotCountAsACapturedRepresentation(string text)
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var document = new Document { CanonicalUrl = "https://example.com/blank-capture" };
        context.Documents.Add(document);
        await context.SaveChangesAsync();
        context.DocumentVersions.Add(new DocumentVersion { DocumentId = document.Id, ContentHash = ContentFingerprint.Compute(text), ExtractedText = text });
        await TestData.AssertRejectedAsync(() => context.SaveChangesAsync(), PostgresErrorCodes.CheckViolation, "ck_document_versions_text_not_blank");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    [InlineData("\v")]
    [InlineData("\u00a0\u2003\u3000")]
    public async Task BlankStorageCannotCountAsARawCapture(string storage)
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var document = new Document { CanonicalUrl = "https://example.com/blank-storage" };
        context.Documents.Add(document);
        await context.SaveChangesAsync();
        context.DocumentVersions.Add(new DocumentVersion
        {
            DocumentId = document.Id,
            ContentHash = ContentFingerprint.ComputeRaw("raw capture"u8),
            StorageUri = storage,
            Metadata = "{\"content_hash\":{\"algorithm\":\"sha256\",\"basis\":\"raw_bytes\"}}"
        });
        await TestData.AssertRejectedAsync(() => context.SaveChangesAsync(), PostgresErrorCodes.CheckViolation, "ck_document_versions_storage_not_blank");
    }

    [Fact]
    public async Task AMeaningfulSingleLetterIsNotMistakenForAWhitespaceEscape()
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var version = await TestData.CreateVersionAsync(context, "v");
        Assert.Equal("v", version.ExtractedText);
    }
}
