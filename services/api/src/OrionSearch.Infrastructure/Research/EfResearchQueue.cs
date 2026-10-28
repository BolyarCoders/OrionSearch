using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OrionSearch.Application.Research;
using OrionSearch.Domain.Jobs;
using OrionSearch.Domain.Research;
using OrionSearch.Infrastructure.Persistence;

namespace OrionSearch.Infrastructure.Research;

internal sealed class EfResearchQueue(IDbContextFactory<OrionSearchDbContext> factory, ResearchWorkerOptions options) : IResearchQueue
{
    public async Task<ResearchLease?> ClaimAsync(string workerId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        await using var context = await factory.CreateDbContextAsync(ct);
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        var job = await context.Jobs.FromSqlRaw("""
            SELECT j.* FROM system.jobs j
            JOIN research.research_runs r ON r.id = j.research_run_id
            JOIN research.archives a ON a.id = r.archive_id
            WHERE j.job_type = 'research_plan' AND j.status = 'pending' AND r.status = 'queued'
                AND j.run_after <= clock_timestamp() AND j.attempts < j.max_attempts AND a.status = 'active'
            ORDER BY j.priority DESC, j.created_at, j.id LIMIT 1 FOR UPDATE OF j SKIP LOCKED
            """).SingleOrDefaultAsync(ct);
        if (job is null) return null;
        var run = await context.ResearchRuns.SingleAsync(run => run.Id == job.ResearchRunId, ct);
        var archive = await LockArchiveAsync(context, run.ArchiveId, ct);
        if (archive.Status != "active") return null;
        var now = await NowAsync(context, ct);
        job.Status = "processing";
        job.WorkerId = workerId;
        job.LockedUntil = now.AddSeconds(options.LeaseSeconds);
        job.Attempts++;
        job.LastError = null;
        run.Status = "running";
        run.StartedAt ??= now;
        run.ErrorMessage = null;
        context.JobAttempts.Add(new JobAttempt { JobId = job.Id, AttemptNumber = job.Attempts, StartedAt = now });
        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new ResearchLease(job.Id, run.Id, run.ArchiveId, job.Attempts, workerId);
    }

    public async Task<AgentRequest?> DispatchAsync(ResearchLease lease, CancellationToken ct)
    {
        await using var context = await factory.CreateDbContextAsync(ct);
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        var job = await OwnedAsync(context, lease, ct);
        if (job is null) return null;
        var archive = await LockArchiveAsync(context, lease.ArchiveId, ct);
        if (archive.Status != "active")
        {
            if (await FinishFailureAsync(context, job, lease, archive.Status == "archived" ? ResearchFailure.Archived : ResearchFailure.Paused, ct))
                await transaction.CommitAsync(ct);
            return null;
        }
        AgentRequest request;
        try
        {
            request = ReadRequest(job, lease);
        }
        catch (Exception exception) when (exception is JsonException or NullReferenceException)
        {
            if (await FinishFailureAsync(context, job, lease, ResearchFailure.Contract, ct)) await transaction.CommitAsync(ct);
            return null;
        }
        await transaction.CommitAsync(ct);
        return request;
    }

    public async Task<bool> HeartbeatAsync(ResearchLease lease, CancellationToken ct)
    {
        await using var context = await factory.CreateDbContextAsync(ct);
        return await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE system.jobs SET locked_until = clock_timestamp() + make_interval(secs => {options.LeaseSeconds})
            WHERE id = {lease.JobId} AND research_run_id = {lease.RunId} AND status = 'processing'
                AND worker_id = {lease.WorkerId} AND attempts = {lease.Attempt} AND locked_until > clock_timestamp()
                AND EXISTS (SELECT 1 FROM research.archives WHERE id = {lease.ArchiveId} AND status <> 'archived')
            """, ct) == 1;
    }

    public async Task<bool> CompleteAsync(ResearchLease lease, AgentResult result, CancellationToken ct)
    {
        if (result is null || !ResearchContract.IsSupported(result.ContractVersion, options.AllowProvisionalContract))
            throw new ResearchAgentException(ResearchFailure.Contract);
        result = ResearchOutputValidator.Normalize(result, lease.RunId, result.ContractVersion);
        await using var context = await factory.CreateDbContextAsync(ct);
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        var job = await OwnedAsync(context, lease, ct);
        if (job is null) return false;
        try
        {
            if (ReadRequest(job, lease).ContractVersion != result.ContractVersion) throw new JsonException();
        }
        catch (Exception exception) when (exception is JsonException or NullReferenceException)
        {
            throw new ResearchAgentException(ResearchFailure.Contract);
        }
        var archive = await LockArchiveAsync(context, lease.ArchiveId, ct);
        if (archive.Status == "archived")
        {
            var cancelled = await FinishFailureAsync(context, job, lease, ResearchFailure.Archived, ct);
            if (cancelled) await transaction.CommitAsync(ct);
            return false;
        }
        await ResearchIngestion.ApplyAsync(context, lease.RunId, lease.ArchiveId, result, ct);
        var run = await context.ResearchRuns.SingleAsync(run => run.Id == lease.RunId, ct);
        run.Status = "completed";
        run.FinishedAt = await NowAsync(context, ct);
        run.ErrorMessage = null;
        run.Stats = JsonSerializer.Serialize(new RunStatistics(result.Queries.Length, result.Sources.Length,
            result.Sources.Count(source => source.Capture is not null), result.Findings.Length), ResearchContract.Json);
        var attempt = await CurrentAttemptAsync(context, lease, ct);
        attempt.Status = "completed";
        attempt.FinishedAt = run.FinishedAt;
        if (!await FinalizeAsync(context, job, lease, "completed", null, job.RunAfter, ct)) return false;
        await transaction.CommitAsync(ct);
        return true;
    }

    public async Task<bool> FailAsync(ResearchLease lease, ResearchFailure failure, CancellationToken ct, int? retryAfterSeconds = null)
    {
        await using var context = await factory.CreateDbContextAsync(ct);
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        var job = await OwnedAsync(context, lease, ct);
        if (job is null) return false;
        var archive = await LockArchiveAsync(context, lease.ArchiveId, ct);
        if (archive.Status == "archived") failure = ResearchFailure.Archived;
        var saved = await FinishFailureAsync(context, job, lease, failure, ct, retryAfterSeconds);
        if (saved) await transaction.CommitAsync(ct);
        return saved;
    }

    public async Task<int> RecoverAsync(CancellationToken ct)
    {
        var recovered = 0;
        for (var index = 0; index < 100; index++)
        {
            await using var context = await factory.CreateDbContextAsync(ct);
            await using var transaction = await context.Database.BeginTransactionAsync(ct);
            var job = await context.Jobs.FromSqlRaw("""
                SELECT j.* FROM system.jobs j
                JOIN research.research_runs r ON r.id = j.research_run_id
                JOIN research.archives a ON a.id = r.archive_id
                WHERE j.job_type = 'research_plan' AND (
                    (j.status = 'processing' AND (j.locked_until IS NULL OR j.locked_until <= clock_timestamp()))
                    OR (j.status = 'pending' AND (a.status = 'archived' OR j.attempts >= j.max_attempts)))
                ORDER BY j.id LIMIT 1 FOR UPDATE OF j SKIP LOCKED
                """).SingleOrDefaultAsync(ct);
            if (job is null) break;
            var run = await context.ResearchRuns.SingleAsync(run => run.Id == job.ResearchRunId, ct);
            var archive = await LockArchiveAsync(context, run.ArchiveId, ct);
            var now = await NowAsync(context, ct);
            var failure = archive.Status == "archived" ? ResearchFailure.Archived : ResearchFailure.Timeout;
            var retry = failure != ResearchFailure.Archived && job.Attempts < job.MaxAttempts;
            if (job.Status == "processing")
            {
                var attempt = await context.JobAttempts.SingleOrDefaultAsync(attempt => attempt.JobId == job.Id && attempt.AttemptNumber == job.Attempts, ct);
                if (attempt is { Status: "running" })
                {
                    attempt.Status = "timed_out";
                    attempt.FinishedAt = now;
                    attempt.ErrorMessage = ResearchFailures.Summary(failure);
                }
            }
            job.Status = failure == ResearchFailure.Archived ? "cancelled" : retry ? "pending" : "failed";
            run.Status = job.Status == "pending" ? "queued" : job.Status;
            job.WorkerId = null;
            job.LockedUntil = null;
            job.LastError = run.ErrorMessage = ResearchFailures.Summary(failure);
            job.RunAfter = now.AddSeconds(options.BackoffSeconds(job.Attempts));
            job.CompletedAt = run.FinishedAt = retry ? null : now;
            await context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            recovered++;
        }
        return recovered;
    }

    private async Task<bool> FinishFailureAsync(OrionSearchDbContext context, Job job, ResearchLease lease, ResearchFailure failure, CancellationToken ct, int? retryAfterSeconds = null)
    {
        var now = await NowAsync(context, ct);
        var retry = ResearchFailures.IsTransient(failure) && job.Attempts < job.MaxAttempts;
        var state = failure == ResearchFailure.Archived ? "cancelled" : retry ? "pending" : "failed";
        var run = await context.ResearchRuns.SingleAsync(run => run.Id == lease.RunId, ct);
        run.Status = state == "pending" ? "queued" : state;
        run.ErrorMessage = ResearchFailures.Summary(failure);
        run.FinishedAt = retry ? null : now;
        var attempt = await CurrentAttemptAsync(context, lease, ct);
        attempt.Status = failure == ResearchFailure.Timeout ? "timed_out" : "failed";
        attempt.FinishedAt = now;
        attempt.ErrorMessage = run.ErrorMessage;
        var delay = Math.Max(options.BackoffSeconds(job.Attempts), Math.Clamp(retryAfterSeconds ?? 0, 0, options.RetryMaxSeconds));
        return await FinalizeAsync(context, job, lease, state, run.ErrorMessage, now.AddSeconds(delay), ct);
    }

    private static async Task<bool> FinalizeAsync(OrionSearchDbContext context, Job job, ResearchLease lease,
        string state, string? error, DateTime runAfter, CancellationToken ct)
    {
        await context.SaveChangesAsync(ct);
        // OwnedAsync validated the live lease after acquiring FOR UPDATE. That lock prevents
        // reassignment until commit/rollback; elapsed time cannot revoke ownership inside this transaction.
        return await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE system.jobs SET status = {state}, last_error = {error}, run_after = {runAfter},
                completed_at = CASE WHEN {state} IN ('completed', 'failed', 'cancelled') THEN clock_timestamp() ELSE NULL END,
                worker_id = NULL, locked_until = NULL
            WHERE id = {job.Id} AND research_run_id = {lease.RunId} AND status = 'processing'
                AND worker_id = {lease.WorkerId} AND attempts = {lease.Attempt}
                AND EXISTS (SELECT 1 FROM research.research_runs r WHERE r.id = {lease.RunId} AND r.archive_id = {lease.ArchiveId})
            """, ct) == 1;
    }

    private static async Task<Job?> OwnedAsync(OrionSearchDbContext context, ResearchLease lease, CancellationToken ct)
    {
        var job = await context.Jobs.FromSql($"""
            SELECT j.* FROM system.jobs j JOIN research.research_runs r ON r.id = j.research_run_id
            WHERE j.id = {lease.JobId} AND j.research_run_id = {lease.RunId} AND r.archive_id = {lease.ArchiveId}
                AND j.status = 'processing' AND j.worker_id = {lease.WorkerId} AND j.attempts = {lease.Attempt}
                AND j.locked_until > clock_timestamp() FOR UPDATE OF j
            """).SingleOrDefaultAsync(ct);
        // The WHERE predicate can be evaluated before waiting for an unchanged locked row.
        // Recheck with database time now that the lock is ours, before accepting ownership.
        return job is not null && job.LockedUntil > await NowAsync(context, ct) ? job : null;
    }

    private AgentRequest ReadRequest(Job job, ResearchLease lease)
    {
        var request = JsonSerializer.Deserialize<ResearchPayload>(job.Payload, ResearchContract.Json)!.Request;
        if (request.ResearchRunId != lease.RunId || request.ArchiveId != lease.ArchiveId
            || !ResearchContract.IsSupported(request.ContractVersion, options.AllowProvisionalContract)) throw new JsonException();
        return request;
    }

    private static Task<Archive> LockArchiveAsync(OrionSearchDbContext context, Guid id, CancellationToken ct) =>
        context.Archives.FromSql($"SELECT * FROM research.archives WHERE id = {id} FOR UPDATE").SingleAsync(ct);
    private static Task<JobAttempt> CurrentAttemptAsync(OrionSearchDbContext context, ResearchLease lease, CancellationToken ct) =>
        context.JobAttempts.SingleAsync(attempt => attempt.JobId == lease.JobId && attempt.AttemptNumber == lease.Attempt, ct);
    private static Task<DateTime> NowAsync(OrionSearchDbContext context, CancellationToken ct) =>
        context.Database.SqlQuery<DateTime>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
}
