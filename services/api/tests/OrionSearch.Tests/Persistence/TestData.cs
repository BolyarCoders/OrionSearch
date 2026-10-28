using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrionSearch.Domain.Auth;
using OrionSearch.Domain.Content;
using OrionSearch.Domain.Research;
using OrionSearch.Infrastructure.Persistence;

namespace OrionSearch.Tests.Persistence;

internal static class TestData
{
    public static async Task<User> CreateUserAsync(OrionSearchDbContext context)
    {
        var user = new User { Email = $"{Guid.NewGuid():N}@example.com", DisplayName = "Researcher" };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    public static async Task<Archive> CreateArchiveAsync(OrionSearchDbContext context)
    {
        var owner = await CreateUserAsync(context);
        var archive = new Archive { OwnerId = owner.Id, Title = "Research archive" };
        context.Archives.Add(archive);
        await context.SaveChangesAsync();
        return archive;
    }

    public static async Task<DocumentVersion> CreateVersionAsync(OrionSearchDbContext context, string text = "Evidence from a source.")
    {
        var document = new Document { CanonicalUrl = $"https://example.com/{Guid.NewGuid():N}" };
        context.Documents.Add(document);
        await context.SaveChangesAsync();
        var version = new DocumentVersion
        {
            DocumentId = document.Id,
            ContentHash = ContentFingerprint.Compute(text),
            ExtractedText = ContentFingerprint.Normalize(text)
        };
        context.DocumentVersions.Add(version);
        await context.SaveChangesAsync();
        return version;
    }

    public static async Task<DocumentChunk> CreateChunkAsync(OrionSearchDbContext context, Guid versionId)
    {
        var chunk = new DocumentChunk { VersionId = versionId, ChunkIndex = 0, Content = "Evidence from a source." };
        context.DocumentChunks.Add(chunk);
        await context.SaveChangesAsync();
        return chunk;
    }

    public static async Task AssertRejectedAsync(Func<Task> action, string sqlState, string? constraint = null)
    {
        var exception = await Assert.ThrowsAsync<DbUpdateException>(action);
        var postgres = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(sqlState, postgres.SqlState);
        if (constraint is not null)
        {
            Assert.Equal(constraint, postgres.ConstraintName);
        }
    }

    public static void AssertRestricted(PostgresException exception, string constraint)
    {
        // PostgreSQL 18 distinguishes RESTRICT from a general foreign-key violation.
        Assert.Contains(exception.SqlState, new[] { PostgresErrorCodes.RestrictViolation, PostgresErrorCodes.ForeignKeyViolation });
        Assert.Equal(constraint, exception.ConstraintName);
    }
}
