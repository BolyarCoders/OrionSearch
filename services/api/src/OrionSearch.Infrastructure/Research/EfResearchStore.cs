using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OrionSearch.Application.Research;
using OrionSearch.Domain.Jobs;
using OrionSearch.Domain.Research;
using OrionSearch.Infrastructure.Persistence;

namespace OrionSearch.Infrastructure.Research;

internal sealed class EfResearchStore(OrionSearchDbContext context) : IResearchStore
{
    public Task<Job?> FindByKeyAsync(string key, CancellationToken ct) =>
        context.Jobs.SingleOrDefaultAsync(job => job.IdempotencyKey == key, ct);
    public Task<ResearchRun?> FindRunAsync(Guid id, CancellationToken ct) =>
        context.ResearchRuns.AsNoTracking().SingleOrDefaultAsync(run => run.Id == id, ct);

    public async Task<ResearchStatus?> StatusAsync(Guid id, CancellationToken ct)
    {
        var row = await (from run in context.ResearchRuns.AsNoTracking()
                         join job in context.Jobs on run.Id equals job.ResearchRunId into jobs
                         from job in jobs.Where(job => job.JobType == "research_plan").DefaultIfEmpty()
                         where run.Id == id
                         select new { Run = run, Job = job }).SingleOrDefaultAsync(ct);
        if (row is null) return null;
        var statistics = new RunStatistics();
        try
        {
            using var json = JsonDocument.Parse(row.Run.Stats);
            int Count(string name) => json.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var count) && count >= 0 ? count : 0;
            statistics = new RunStatistics(Count("queries"), Count("sources"), Count("versions"), Count("findings"));
        }
        catch (JsonException) { }
        var next = row.Job?.Status == "pending" ? row.Job.RunAfter : (DateTime?)null;
        return new ResearchStatus(row.Run.Id, row.Run.ArchiveId, row.Run.Status, row.Run.CreatedAt,
            row.Run.StartedAt, row.Run.FinishedAt, statistics, row.Job?.Attempts ?? 0, row.Job?.MaxAttempts ?? 0,
            next, row.Run.Status == "queued" && row.Job?.Attempts > 0, ResearchFailures.Sanitize(row.Run.ErrorMessage));
    }

    public void Add(ResearchRun run, Job job)
    {
        context.ResearchRuns.Add(run);
        context.Jobs.Add(job);
    }
    public async Task SaveAsync(CancellationToken ct) => await context.SaveChangesAsync(ct);
}
