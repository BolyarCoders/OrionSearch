using Microsoft.EntityFrameworkCore;
using OrionSearch.Application.Archives;
using OrionSearch.Application.Research;
using OrionSearch.Infrastructure.Persistence;

namespace OrionSearch.Infrastructure.Research;

internal sealed class EfResearchAdmission(OrionSearchDbContext context, ResearchLimitOptions limits) : IResearchAdmission
{
    public async Task<DateTime> RequireCapacityAsync(Guid ownerId, Guid archiveId, CancellationToken ct)
    {
        if (context.Database.CurrentTransaction is null) throw new InvalidOperationException("Admission requires a transaction.");
        await context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(713904, 1)", ct);
        var now = await context.Database.SqlQuery<DateTime>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
        var day = now.Date;
        var end = day.AddDays(1);
        var ownerRuns = context.ResearchRuns.Where(run => context.Archives.Any(archive => archive.Id == run.ArchiveId && archive.OwnerId == ownerId));
        var today = context.ResearchRuns.Where(run => run.CreatedAt >= day && run.CreatedAt < end);
        var dailyRetry = Math.Clamp((int)Math.Ceiling((end - now).TotalSeconds), 1, 86400);
        if (await today.CountAsync(ct) >= limits.MaxRunsGlobalPerDay
            || await ownerRuns.CountAsync(run => run.CreatedAt >= day && run.CreatedAt < end, ct) >= limits.MaxRunsPerOwnerPerDay)
            throw Refused(dailyRetry);
        if (await ownerRuns.CountAsync(run => run.CreatedAt > now.AddMinutes(-1), ct) >= limits.MaxRunsPerOwnerPerMinute)
            throw Refused(60);
        var outstanding = context.Jobs.Where(job => job.JobType == "research_plan" && (job.Status == "pending" || job.Status == "processing"));
        if (await outstanding.CountAsync(ct) >= limits.MaxOutstandingGlobal
            || await outstanding.CountAsync(job => context.ResearchRuns.Any(run => run.Id == job.ResearchRunId && run.ArchiveId == archiveId), ct) >= limits.MaxOutstandingPerArchive
            || await outstanding.CountAsync(job => ownerRuns.Any(run => run.Id == job.ResearchRunId), ct) >= limits.MaxOutstandingPerOwner)
            throw Refused(60);
        return now;
    }

    private static ArchiveRequestException Refused(int retry) =>
        new(429, "Research capacity is temporarily exhausted. Retry later.", retryAfterSeconds: retry);
}
