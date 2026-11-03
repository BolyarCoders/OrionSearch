using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using OrionSearch.Domain.Content;
using OrionSearch.Domain.Knowledge;
using OrionSearch.Domain.Research;

namespace OrionSearch.Tests.Persistence;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class CaptureGrantMigrationTests(PostgreSqlFixture database)
{
    [Fact]
    public async Task UpgradeBackfillsOnlyProvenEvidenceWithoutGrantingLegacyRawRepresentations()
    {
        var connection = await database.CreateDatabaseAsync();
        await using var context = database.CreateContext(connection);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20261028193247_AddNativeIdentity");
        var archive = await TestData.CreateArchiveAsync(context);
        var other = await TestData.CreateArchiveAsync(context);
        var version = await TestData.CreateVersionAsync(context);
        var unknown = await TestData.CreateVersionAsync(context);
        version.StorageUri = "urn:legacy:unverified";
        context.ArchiveDocuments.AddRange(
            new ArchiveDocument { ArchiveId = archive.Id, DocumentId = version.DocumentId },
            new ArchiveDocument { ArchiveId = archive.Id, DocumentId = unknown.DocumentId },
            new ArchiveDocument { ArchiveId = other.Id, DocumentId = version.DocumentId });
        await context.SaveChangesAsync();
        // Seed through the old schema: the current EF model also contains fields
        // introduced by migrations that have not been applied yet.
        var runId = Guid.NewGuid();
        await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO research.research_runs (id, archive_id) VALUES ({runId}, {archive.Id})");
        var finding = new Finding { Statement = "Existing finding", ResearchRunId = runId };
        context.Findings.Add(finding);
        await context.SaveChangesAsync();
        context.FindingEvidence.Add(new FindingEvidence { FindingId = finding.Id, DocumentVersionId = version.Id });
        await context.SaveChangesAsync();

        await migrator.MigrateAsync();
        await migrator.MigrateAsync();
        var grant = await context.ArchiveDocumentVersions.SingleAsync();
        Assert.Equal(archive.Id, grant.ArchiveId);
        Assert.Equal(version.Id, grant.DocumentVersionId);
        Assert.Null(grant.StorageUri);
        Assert.Null(grant.RawSha256);
        Assert.Equal("urn:legacy:unverified", (await context.DocumentVersions.SingleAsync(v => v.Id == version.Id)).StorageUri);
        Assert.Single(await context.FindingEvidence.ToListAsync());
        Assert.False(context.Database.HasPendingModelChanges());
        var rollback = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync("20261028193247_AddNativeIdentity"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, rollback.SqlState);
        Assert.Single(await context.ArchiveDocumentVersions.ToListAsync());
    }

    [Fact]
    public async Task DatabaseRejectsGrantsForUnlinkedDocumentsAndMismatchedVersions()
    {
        var connection = await database.CreateDatabaseAsync();
        await using var context = database.CreateContext(connection);
        await context.Database.MigrateAsync();
        var archive = await TestData.CreateArchiveAsync(context);
        var first = await TestData.CreateVersionAsync(context);
        var other = await TestData.CreateVersionAsync(context);
        context.ArchiveDocuments.Add(new ArchiveDocument { ArchiveId = archive.Id, DocumentId = first.DocumentId });
        await context.SaveChangesAsync();
        context.ArchiveDocumentVersions.Add(new ArchiveDocumentVersion { ArchiveId = archive.Id, DocumentId = first.DocumentId, DocumentVersionId = other.Id });
        await TestData.AssertRejectedAsync(() => context.SaveChangesAsync(), PostgresErrorCodes.ForeignKeyViolation);
        context.ChangeTracker.Clear();
        context.ArchiveDocumentVersions.Add(new ArchiveDocumentVersion { ArchiveId = archive.Id, DocumentId = other.DocumentId, DocumentVersionId = other.Id });
        await TestData.AssertRejectedAsync(() => context.SaveChangesAsync(), PostgresErrorCodes.ForeignKeyViolation);
        context.ChangeTracker.Clear();
        context.ArchiveDocumentVersions.Add(new ArchiveDocumentVersion { ArchiveId = archive.Id, DocumentId = first.DocumentId, DocumentVersionId = first.Id, StorageUri = "s3://bad" });
        await TestData.AssertRejectedAsync(() => context.SaveChangesAsync(), PostgresErrorCodes.CheckViolation, "ck_archive_versions_raw");
    }
}
