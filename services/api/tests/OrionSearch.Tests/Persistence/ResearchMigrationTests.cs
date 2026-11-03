using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using OrionSearch.Domain.Content;
using OrionSearch.Tests.Archives;
using OrionSearch.Tests.Research;

namespace OrionSearch.Tests.Persistence;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class ResearchMigrationTests(PostgreSqlFixture database)
{
    private const string PreviousMigration = "20261016095458_AllowRawOnlyDocumentVersions";

    [Fact]
    public async Task ReportMigrationUpgradesPreviousSchema()
    {
        var connectionString = await database.CreateDatabaseAsync();
        await using var context = database.CreateContext(connectionString);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20261029064332_IndexResearchAdmissionBudgets");
        Assert.Equal(0, await context.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM information_schema.columns
            WHERE table_schema = 'research' AND table_name = 'research_runs' AND column_name = 'report'
            """).SingleAsync());
        await migrator.MigrateAsync();
        Assert.Equal(1, await context.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM information_schema.columns
            WHERE table_schema = 'research' AND table_name = 'research_runs' AND column_name = 'report'
            """).SingleAsync());
    }

    [Fact]
    public async Task ReportMigrationPreservesExistingResearchRuns()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        var run = await ResearchTestData.SubmitAsync(scenario);
        await using var context = scenario.Context();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20261029064332_IndexResearchAdmissionBudgets");
        await migrator.MigrateAsync();
        var stored = await context.ResearchRuns.AsNoTracking().SingleAsync(item => item.Id == run.Id);
        Assert.Equal("queued", stored.Status);
        Assert.Null(stored.Report);
    }

    [Fact]
    public async Task ReportMigrationDowngradeRefusesToDiscardStoredReports()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        var run = await ResearchTestData.SubmitAsync(scenario);
        await using var scope = scenario.Scope();
        var queue = ResearchTestData.Queue(scope);
        var lease = (await queue.ClaimAsync("report-worker", default))!;
        Assert.True(await queue.CompleteAsync(lease, ResearchTestData.Output(run.Id) with { Report = "Saved report" }, default));
        await using var context = scenario.Context();
        var error = await Assert.ThrowsAsync<PostgresException>(() => context.GetService<IMigrator>().MigrateAsync("20261029064332_IndexResearchAdmissionBudgets"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        Assert.Equal("Saved report", (await context.ResearchRuns.SingleAsync()).Report);
    }

    [Fact]
    public async Task UpgradeRefusesLegacyBlankCapturesWithoutRewritingOrDeletingThem()
    {
        var connectionString = await database.CreateDatabaseAsync();
        await using var context = database.CreateContext(connectionString);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);
        var document = new Document { CanonicalUrl = "https://example.com/legacy-blank" };
        context.Documents.Add(document);
        await context.SaveChangesAsync();
        var version = new DocumentVersion { DocumentId = document.Id, ContentHash = ContentFingerprint.Compute(""), ExtractedText = "" };
        context.DocumentVersions.Add(version);
        await context.SaveChangesAsync();
        var exception = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal(7, (await context.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Equal("", (await context.DocumentVersions.AsNoTracking().SingleAsync()).ExtractedText);
        var addedColumn = await context.Database.SqlQuery<int>($"""
            SELECT count(*)::integer AS "Value" FROM information_schema.columns
            WHERE table_schema = 'knowledge' AND table_name = 'findings' AND column_name = 'research_run_id'
            """).SingleAsync();
        Assert.Equal(0, addedColumn);
    }

    [Fact]
    public async Task DowngradeCannotDiscardNewFindingProvenance()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        var run = await ResearchTestData.SubmitAsync(scenario);
        await using var scope = scenario.Scope();
        var queue = ResearchTestData.Queue(scope);
        var lease = (await queue.ClaimAsync("provenance-worker", default))!;
        Assert.True(await queue.CompleteAsync(lease, ResearchTestData.Output(run.Id), default));
        await using var context = scenario.Context();
        var exception = await Assert.ThrowsAsync<PostgresException>(() => context.GetService<IMigrator>().MigrateAsync(PreviousMigration));
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal(run.Id, (await context.Findings.SingleAsync()).ResearchRunId);
        Assert.Equal("20261028220005_ScopeCapturedVersionsToArchives", (await context.Database.GetAppliedMigrationsAsync()).Last());
        Assert.Single(await context.FindingEvidence.ToListAsync());
        await context.GetService<IMigrator>().MigrateAsync();
        Assert.Equal(context.Database.GetMigrations().Count(), (await context.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Equal(run.Id, (await context.Findings.SingleAsync()).ResearchRunId);
    }
}
