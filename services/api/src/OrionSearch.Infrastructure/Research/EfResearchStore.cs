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
        return ResearchStatusMapping.Map(row.Run, row.Job);
    }

    public void Add(ResearchRun run, Job job)
    {
        context.ResearchRuns.Add(run);
        context.Jobs.Add(job);
    }
    public async Task SaveAsync(CancellationToken ct) => await context.SaveChangesAsync(ct);
}
