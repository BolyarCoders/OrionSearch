using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrionSearch.Domain.Jobs;
using OrionSearch.Domain.Research;

namespace OrionSearch.Tests.Persistence;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class ResearchAndJobPersistenceTests(PostgreSqlFixture database)
{
    [Fact]
    public async Task RunQueryJobAndAttemptPreserveTheirRelationshipsAndDefaults()
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var archive = await TestData.CreateArchiveAsync(context);
        var run = new ResearchRun { ArchiveId = archive.Id };
        context.ResearchRuns.Add(run);
        await context.SaveChangesAsync();
        var query = new ResearchQuery { RunId = run.Id, Query = "PostgreSQL integrity", Provider = "test-provider" };
        var job = new Job { ResearchRunId = run.Id, JobType = "web_search", Payload = "{\"query\":\"PostgreSQL integrity\"}" };
        context.ResearchQueries.Add(query);
        context.Jobs.Add(job);
        await context.SaveChangesAsync();
        var attempt = new JobAttempt { JobId = job.Id, AttemptNumber = 1 };
        context.JobAttempts.Add(attempt);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var storedJob = await context.Jobs.SingleAsync(x => x.Id == job.Id);
        using var payload = JsonDocument.Parse(storedJob.Payload);
        Assert.Equal(run.Id, storedJob.ResearchRunId);
        Assert.Equal("pending", storedJob.Status);
        Assert.Equal(0, storedJob.Attempts);
        Assert.Equal(3, storedJob.MaxAttempts);
        Assert.Equal(0, storedJob.Priority);
        Assert.Equal(DateTimeKind.Utc, storedJob.RunAfter.Kind);
        Assert.Equal("PostgreSQL integrity", payload.RootElement.GetProperty("query").GetString());
        Assert.Equal("{}", (await context.ResearchRuns.SingleAsync(x => x.Id == run.Id)).Stats);
        Assert.Equal("queued", (await context.ResearchQueries.SingleAsync(x => x.Id == query.Id)).Status);
        Assert.Equal("running", (await context.JobAttempts.SingleAsync(x => x.Id == attempt.Id)).Status);
    }

    [Fact]
    public async Task RunRequiresAnExistingArchive()
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        context.ResearchRuns.Add(new ResearchRun { ArchiveId = Guid.NewGuid() });

        await TestData.AssertRejectedAsync(() => context.SaveChangesAsync(), PostgresErrorCodes.ForeignKeyViolation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidRunStatusAndTriggerAreRejected(bool invalidTrigger)
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var archive = await TestData.CreateArchiveAsync(context);
        context.ResearchRuns.Add(new ResearchRun
        {
            ArchiveId = archive.Id,
            Status = invalidTrigger ? "queued" : "invalid",
            TriggerType = invalidTrigger ? "invalid" : "manual"
        });

        await TestData.AssertRejectedAsync(() => context.SaveChangesAsync(), PostgresErrorCodes.CheckViolation,
            invalidTrigger ? "ck_research_runs_trigger" : "ck_research_runs_status");
    }

    [Fact]
    public async Task InvalidQueryStatusIsRejected()
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var archive = await TestData.CreateArchiveAsync(context);
        var run = new ResearchRun { ArchiveId = archive.Id };
        context.ResearchRuns.Add(run);
        await context.SaveChangesAsync();
        context.ResearchQueries.Add(new ResearchQuery { RunId = run.Id, Query = "A query", Provider = "test-provider", Status = "cancelled" });

        await TestData.AssertRejectedAsync(() => context.SaveChangesAsync(), PostgresErrorCodes.CheckViolation, "ck_research_queries_status");
    }

    [Theory]
    [InlineData(-1, 3)]
    [InlineData(0, 0)]
    [InlineData(0, -1)]
    [InlineData(4, 3)]
    public async Task InvalidAttemptCountersAreRejected(int attempts, int maxAttempts)
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        context.Jobs.Add(new Job { JobType = "research_plan", Attempts = attempts, MaxAttempts = maxAttempts });

        await TestData.AssertRejectedAsync(() => context.SaveChangesAsync(), PostgresErrorCodes.CheckViolation, "ck_jobs_attempts");
    }

    [Theory]
    [InlineData("invalid", "research_plan", "ck_jobs_status")]
    [InlineData("pending", "unknown_job", "ck_jobs_type")]
    public async Task InvalidJobStatusesAndTypesAreRejected(string status, string jobType, string constraint)
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        context.Jobs.Add(new Job { JobType = jobType, Status = status });

        await TestData.AssertRejectedAsync(() => context.SaveChangesAsync(), PostgresErrorCodes.CheckViolation, constraint);
    }

    [Fact]
    public async Task NonNullIdempotencyKeysAreUnique()
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        context.Jobs.Add(new Job { JobType = "research_plan", IdempotencyKey = "same-request" });
        await context.SaveChangesAsync();
        context.Jobs.Add(new Job { JobType = "research_plan", IdempotencyKey = "same-request" });

        await TestData.AssertRejectedAsync(() => context.SaveChangesAsync(), PostgresErrorCodes.UniqueViolation, "ux_jobs_idempotency");
    }

    [Theory]
    [InlineData("[]", PostgresErrorCodes.CheckViolation, "ck_jobs_payload")]
    [InlineData("not json", PostgresErrorCodes.InvalidTextRepresentation, null)]
    public async Task InvalidJsonPayloadsAreRejected(string payload, string sqlState, string? constraint)
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        context.Jobs.Add(new Job { JobType = "research_plan", Payload = payload });

        await TestData.AssertRejectedAsync(() => context.SaveChangesAsync(), sqlState, constraint);
    }

    [Fact]
    public async Task JobsCanOmitIdempotencyKeysAndResearchRuns()
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        context.Jobs.AddRange(new Job { JobType = "fetch_document" }, new Job { JobType = "extract_content" });

        await context.SaveChangesAsync();

        Assert.Equal(2, await context.Jobs.CountAsync(x => x.IdempotencyKey == null && x.ResearchRunId == null));
    }

    [Fact]
    public async Task DuplicateAttemptNumbersForTheSameJobAreRejected()
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var job = new Job { JobType = "research_plan" };
        context.Jobs.Add(job);
        await context.SaveChangesAsync();
        context.JobAttempts.Add(new JobAttempt { JobId = job.Id, AttemptNumber = 1 });
        await context.SaveChangesAsync();
        context.JobAttempts.Add(new JobAttempt { JobId = job.Id, AttemptNumber = 1 });

        await TestData.AssertRejectedAsync(() => context.SaveChangesAsync(), PostgresErrorCodes.UniqueViolation, "ux_job_attempts_number");
    }

    [Theory]
    [InlineData(0, "running", "ck_job_attempts_number")]
    [InlineData(-1, "running", "ck_job_attempts_number")]
    [InlineData(1, "invalid", "ck_job_attempts_status")]
    public async Task InvalidJobAttemptsAreRejected(int number, string status, string constraint)
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var job = new Job { JobType = "research_plan" };
        context.Jobs.Add(job);
        await context.SaveChangesAsync();
        context.JobAttempts.Add(new JobAttempt { JobId = job.Id, AttemptNumber = number, Status = status });

        await TestData.AssertRejectedAsync(() => context.SaveChangesAsync(), PostgresErrorCodes.CheckViolation, constraint);
    }

    [Fact]
    public async Task ArchiveResearchHistoryCannotBeDeletedByCascade()
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var archive = await TestData.CreateArchiveAsync(context);
        context.ResearchRuns.Add(new ResearchRun { ArchiveId = archive.Id });
        await context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => context.Archives.Where(x => x.Id == archive.Id).ExecuteDeleteAsync());

        TestData.AssertRestricted(exception, "FK_research_runs_archives_archive_id");
    }

    [Fact]
    public async Task IndependentTransactionsCanSelectPendingJobsWithSkipLocked()
    {
        var connectionString = await database.CreateDatabaseAsync();
        await using var context = database.CreateContext(connectionString);
        await context.Database.MigrateAsync();
        var firstJob = new Job { JobType = "research_plan", Priority = 10 };
        var secondJob = new Job { JobType = "research_plan", Priority = 5 };
        context.Jobs.AddRange(firstJob, secondJob);
        await context.SaveChangesAsync();
        await using var firstConnection = new NpgsqlConnection(connectionString);
        await using var secondConnection = new NpgsqlConnection(connectionString);
        await firstConnection.OpenAsync();
        await secondConnection.OpenAsync();
        await using var firstTransaction = await firstConnection.BeginTransactionAsync();
        await using var secondTransaction = await secondConnection.BeginTransactionAsync();
        const string sql = """
            SELECT id FROM system.jobs WHERE status = 'pending' AND run_after <= now() AND attempts < max_attempts
            ORDER BY priority DESC, created_at ASC LIMIT 1 FOR UPDATE SKIP LOCKED
            """;
        await using var firstCommand = new NpgsqlCommand(sql, firstConnection, firstTransaction);
        await using var secondCommand = new NpgsqlCommand(sql, secondConnection, secondTransaction);

        Assert.Equal(firstJob.Id, await firstCommand.ExecuteScalarAsync());
        Assert.Equal(secondJob.Id, await secondCommand.ExecuteScalarAsync());
    }
}
