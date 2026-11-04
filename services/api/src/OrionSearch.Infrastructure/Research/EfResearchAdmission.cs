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
        var ownerRuns = context.ResearchRuns.Where(run => context.Archives.Any(archive => archive.Id == run.ArchiveId && archive.OwnerId == ownerId));
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
