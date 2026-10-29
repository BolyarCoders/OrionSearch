using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using OrionSearch.Domain.Content;
using OrionSearch.Domain.Knowledge;

namespace OrionSearch.Tests.Persistence;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class MigrationTests(PostgreSqlFixture database)
{
    [Fact]
    public async Task MigrationsCreateExactlyTheMvpTablesAndAllSevenSchemas()
    {
        var schemas = await ReadStringsAsync(database.ConnectionString,
            "SELECT nspname FROM pg_namespace WHERE nspname IN ('auth', 'research', 'content', 'knowledge', 'ai', 'publishing', 'system') ORDER BY nspname");
        Assert.Equal(new[] { "ai", "auth", "content", "knowledge", "publishing", "research", "system" }, schemas);

        var tables = await ReadStringsAsync(database.ConnectionString, TableQuery);
        Assert.Equal(new[]
        {
            "auth.archive_members", "auth.users", "content.archive_document_versions", "content.archive_documents", "content.document_chunks",
            "content.document_versions", "content.documents", "content.publishers", "knowledge.finding_evidence",
            "knowledge.findings", "research.archive_settings", "research.archives", "research.research_queries",
            "research.research_runs", "system.data_protection_keys", "system.job_attempts", "system.jobs"
        }, tables);

        await using var context = database.CreateContext();
        Assert.Equal(context.Database.GetMigrations().Count(), (await context.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task FreshMigrationsAreReproducibleAndReapplyingPreservesData()
    {
        var connectionString = await database.CreateDatabaseAsync();
        await using var context = database.CreateContext(connectionString);
        await context.Database.MigrateAsync();
        var user = await TestData.CreateUserAsync(context);
        await context.Database.MigrateAsync();

        Assert.True(await context.Users.AnyAsync(x => x.Id == user.Id));
        Assert.Equal(await ReadStringsAsync(database.ConnectionString, CatalogQuery),
            await ReadStringsAsync(connectionString, CatalogQuery));
    }

    [Fact]
    public async Task UpgradingTheUsersMigrationPreservesExistingUsers()
    {
        var connectionString = await database.CreateDatabaseAsync();
        await using var context = database.CreateContext(connectionString);
        var migrator = context.GetService<IMigrator>();
        var firstMigration = context.Database.GetMigrations().First();
        await migrator.MigrateAsync(firstMigration);
        var userId = Guid.NewGuid();
        // Seed the historical shape, before Identity columns exist.
        await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO auth.users (id, email, display_name) VALUES ({userId}, {"legacy@example.com"}, {"Legacy user"})");

        await migrator.MigrateAsync();

        Assert.True(await context.Users.AnyAsync(x => x.Id == userId));
        Assert.Equal(17, (await ReadStringsAsync(connectionString, TableQuery)).Length);
    }

    [Fact]
    public async Task IdempotentSqlCanInitializeAnEmptyDatabaseAndRunTwice()
    {
        var connectionString = await database.CreateDatabaseAsync();
        await using var context = database.CreateContext(connectionString);
        var script = context.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(script, connection) { CommandTimeout = 60 };
        await command.ExecuteNonQueryAsync();
        await command.ExecuteNonQueryAsync();

        Assert.Equal(17, (await ReadStringsAsync(connectionString, TableQuery)).Length);
        Assert.Equal(context.Database.GetMigrations().Count(), (await context.Database.GetAppliedMigrationsAsync()).Count());
    }

    [Fact]
    public async Task UpgradingTheSixMigrationSchemaPreservesVersionsHashesAndEvidence()
    {
        var connectionString = await database.CreateDatabaseAsync();
        await using var context = database.CreateContext(connectionString);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20261016091819_CreateJobQueue");
        var version = await TestData.CreateVersionAsync(context);
        var chunk = await TestData.CreateChunkAsync(context, version.Id);
        var finding = new Finding { Id = Guid.NewGuid(), Statement = "Existing evidence-backed finding" };
        // Seed the shipped schema with its original columns, before the new model's run-provenance column exists.
        await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO knowledge.findings (id, statement) VALUES ({finding.Id}, {finding.Statement})");
        var evidence = new FindingEvidence { FindingId = finding.Id, DocumentVersionId = version.Id, ChunkId = chunk.Id };
        context.FindingEvidence.Add(evidence);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        await migrator.MigrateAsync();

        var storedVersion = await context.DocumentVersions.SingleAsync(x => x.Id == version.Id);
        var storedEvidence = await context.FindingEvidence.SingleAsync(x => x.Id == evidence.Id);
        Assert.Equal(version.ExtractedText, storedVersion.ExtractedText);
        Assert.Equal(version.ContentHash, storedVersion.ContentHash);
        Assert.Equal(version.FetchedAt, storedVersion.FetchedAt);
        Assert.Equal("{}", storedVersion.Metadata);
        Assert.Equal(version.Id, storedEvidence.DocumentVersionId);
        Assert.Equal(chunk.Id, storedEvidence.ChunkId);
        context.DocumentVersions.Add(new DocumentVersion
        {
            DocumentId = version.DocumentId,
            ContentHash = ContentFingerprint.ComputeRaw("captured bytes"u8),
            StorageUri = "urn:orion:test:upgraded-raw-capture",
            Metadata = "{\"content_hash\":{\"algorithm\":\"sha256\",\"basis\":\"raw_bytes\"}}"
        });
        await context.SaveChangesAsync();
        Assert.Equal(2, await context.DocumentVersions.CountAsync());
        Assert.Equal(context.Database.GetMigrations().Count(), (await context.Database.GetAppliedMigrationsAsync()).Count());
    }

    [Fact]
    public async Task DowngradingWithRawOnlyVersionsFailsWithoutInventingTextOrDeletingData()
    {
        var connectionString = await database.CreateDatabaseAsync();
        await using var context = database.CreateContext(connectionString);
        await context.Database.MigrateAsync();
        var document = new Document { CanonicalUrl = "https://example.com/raw-rollback" };
        context.Documents.Add(document);
        await context.SaveChangesAsync();
        var rawVersion = new DocumentVersion
        {
            DocumentId = document.Id,
            ContentHash = ContentFingerprint.ComputeRaw("preserved raw bytes"u8),
            StorageUri = "urn:orion:test:retained-raw-capture",
            Metadata = "{\"content_hash\":{\"algorithm\":\"sha256\",\"basis\":\"raw_bytes\"}}"
        };
        context.DocumentVersions.Add(rawVersion);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            context.GetService<IMigrator>().MigrateAsync("20261016091819_CreateJobQueue"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        var retainedVersion = await context.DocumentVersions.SingleAsync(x => x.Id == rawVersion.Id);
        Assert.Null(retainedVersion.ExtractedText);
        Assert.Equal(rawVersion.ContentHash, retainedVersion.ContentHash);
        Assert.Equal(7, (await context.Database.GetAppliedMigrationsAsync()).Count());
    }

    [Fact]
    public async Task EmptyDatabaseCanBeRolledBackAndMigratedAgain()
    {
        var connectionString = await database.CreateDatabaseAsync();
        await using var context = database.CreateContext(connectionString);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync();
        await migrator.MigrateAsync("0");
        Assert.Empty(await ReadStringsAsync(connectionString, TableQuery));

        await migrator.MigrateAsync();

        Assert.Equal(17, (await ReadStringsAsync(connectionString, TableQuery)).Length);
    }

    [Fact]
    public async Task DatabaseContainsTheRequiredPartialAndFullTextIndexes()
    {
        var indexes = await ReadStringsAsync(database.ConnectionString,
            "SELECT indexname || ':' || indexdef FROM pg_indexes WHERE schemaname IN ('content', 'research', 'knowledge', 'system')");
        Assert.Contains(indexes, x => x.StartsWith("idx_jobs_ready:") && x.Contains("priority DESC, created_at") && x.Contains("pending"));
        Assert.Contains(indexes, x => x.StartsWith("idx_jobs_run_after:") && x.Contains("pending"));
        Assert.Contains(indexes, x => x.StartsWith("idx_jobs_expired_leases:") && x.Contains("processing"));
        Assert.Contains(indexes, x => x.StartsWith("idx_document_versions_document_fetched:") && x.Contains("document_id, fetched_at DESC"));
        Assert.Contains(indexes, x => x.StartsWith("idx_research_runs_archive_created:") && x.Contains("archive_id, created_at DESC"));
        Assert.Contains(indexes, x => x.StartsWith("idx_findings_first_observed:") && x.Contains("first_observed_at DESC"));
        Assert.Contains(indexes, x => x.StartsWith("idx_chunks_fts:") && x.Contains("USING gin") && x.Contains("to_tsvector('simple'::regconfig, content)"));
    }

    private const string TableQuery = """
        SELECT table_schema || '.' || table_name FROM information_schema.tables
        WHERE table_schema IN ('auth', 'research', 'content', 'knowledge', 'ai', 'publishing', 'system')
          AND table_type = 'BASE TABLE' AND table_name <> '__ef_migrations_history'
        ORDER BY table_schema, table_name
        """;

    private const string CatalogQuery = """
        SELECT 'column:' || table_schema || '.' || table_name || '.' || column_name || ':' || data_type || ':' || is_nullable || ':' || coalesce(column_default, '')
        FROM information_schema.columns WHERE table_schema IN ('auth', 'research', 'content', 'knowledge', 'system')
        UNION ALL
        SELECT 'index:' || schemaname || '.' || indexname || ':' || indexdef FROM pg_indexes
        WHERE schemaname IN ('auth', 'research', 'content', 'knowledge', 'system')
        UNION ALL
        SELECT 'constraint:' || n.nspname || '.' || c.conname || ':' || pg_get_constraintdef(c.oid)
        FROM pg_constraint c JOIN pg_namespace n ON n.oid = c.connamespace
        WHERE n.nspname IN ('auth', 'research', 'content', 'knowledge', 'system')
        ORDER BY 1
        """;

    private static async Task<string[]> ReadStringsAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }
        return values.ToArray();
    }
}
