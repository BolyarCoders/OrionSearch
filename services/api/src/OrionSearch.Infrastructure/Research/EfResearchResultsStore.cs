using Microsoft.EntityFrameworkCore;
using OrionSearch.Application.Archives;
using OrionSearch.Application.Research;
using OrionSearch.Domain.Knowledge;
using OrionSearch.Infrastructure.Persistence;

namespace OrionSearch.Infrastructure.Research;

internal sealed class EfResearchResultsStore(OrionSearchDbContext context) : IResearchResultsStore
{
    public async Task<Page<ResearchStatus>> HistoryAsync(Guid archiveId, ResearchHistoryQuery query, CancellationToken ct)
    {
        var runs = context.ResearchRuns.AsNoTracking().Where(run => run.ArchiveId == archiveId);
        if (query.Status is not null) runs = runs.Where(run => run.Status == query.Status);
        var count = await runs.CountAsync(ct);
        var page = runs.OrderByDescending(run => run.CreatedAt).ThenBy(run => run.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize);
        var rows = await (from run in page
                          join job in context.Jobs on run.Id equals job.ResearchRunId into jobs
                          from job in jobs.Where(job => job.JobType == "research_plan").DefaultIfEmpty()
                          orderby run.CreatedAt descending, run.Id
                          select new { Run = run, Job = job }).ToArrayAsync(ct);
        return new Page<ResearchStatus>(rows.Select(row => ResearchStatusMapping.Map(row.Run, row.Job)).ToArray(), query.Page, query.PageSize, count);
    }

    public Task<bool> HasRunAsync(Guid archiveId, Guid runId, CancellationToken ct) =>
        context.ResearchRuns.AnyAsync(run => run.Id == runId && run.ArchiveId == archiveId, ct);

    public async Task<Page<FindingSummary>> FindingsAsync(Guid archiveId, FindingListQuery query, CancellationToken ct)
    {
        var findings = Findings(archiveId);
        if (query.Status is not null) findings = findings.Where(finding => finding.Status == query.Status);
        if (query.Type is not null) findings = findings.Where(finding => finding.FindingType == query.Type);
        if (query.RunId is { } run) findings = findings.Where(finding => finding.ResearchRunId == run);
        var count = await findings.CountAsync(ct);
        var items = await Summaries(findings.OrderByDescending(finding => finding.FirstObservedAt).ThenBy(finding => finding.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize), archiveId).ToArrayAsync(ct);
        return new Page<FindingSummary>(items, query.Page, query.PageSize, count);
    }

    public async Task<FindingDetails?> FindingAsync(Guid archiveId, Guid findingId, int page, int pageSize, CancellationToken ct)
    {
        var summary = await Summaries(Findings(archiveId).Where(finding => finding.Id == findingId), archiveId).SingleOrDefaultAsync(ct);
        if (summary is null) return null;
        var evidence = context.FindingEvidence.AsNoTracking().Where(item => item.FindingId == findingId
            && context.ArchiveDocumentVersions.Any(grant => grant.ArchiveId == archiveId && grant.DocumentVersionId == item.DocumentVersionId));
        var count = await evidence.CountAsync(ct);
        var selected = evidence.OrderBy(item => item.Id).Skip((page - 1) * pageSize).Take(pageSize);
        var items = await (from item in selected
                           join version in context.DocumentVersions on item.DocumentVersionId equals version.Id
                           join document in context.Documents on version.DocumentId equals document.Id
                           join chunk in context.DocumentChunks on item.ChunkId equals chunk.Id into chunks
                           from chunk in chunks.DefaultIfEmpty()
                           orderby item.Id
                           select new EvidenceReference(item.Id, document.Id, version.Id, item.ChunkId,
                               chunk == null ? null : chunk.ChunkIndex, item.Stance,
                               item.EvidenceText == null ? null : item.EvidenceText!.Substring(0, Math.Min(10000, item.EvidenceText!.Length)),
                               item.EvidenceText != null && item.EvidenceText!.Length > 10000,
                               document.CanonicalUrl, document.Title == null ? null : document.Title!.Substring(0, Math.Min(2000, document.Title!.Length))))
            .ToArrayAsync(ct);
        return new FindingDetails(summary, new Page<EvidenceReference>(items, page, pageSize, count));
    }

    public Task<SourceDetails?> SourceAsync(Guid archiveId, Guid documentId, CancellationToken ct) =>
        (from link in context.ArchiveDocuments.AsNoTracking()
         join document in context.Documents on link.DocumentId equals document.Id
         join publisher in context.Publishers on document.PublisherId equals publisher.Id into publishers
         from publisher in publishers.DefaultIfEmpty()
         where link.ArchiveId == archiveId && document.Id == documentId
         select new SourceDetails(new SourceSummary(document.Id, document.CanonicalUrl, document.Title,
             publisher == null ? null : publisher.Name, publisher == null ? null : publisher.Domain,
             document.Language, document.PublishedAt, document.FirstSeenAt, document.LastSeenAt, link.RelevanceScore, link.AddedAt),
             context.ArchiveDocumentVersions.Count(grant => grant.ArchiveId == archiveId && grant.DocumentId == documentId))).SingleOrDefaultAsync(ct);

    private IQueryable<Finding> Findings(Guid archiveId) => context.Findings.AsNoTracking().Where(finding =>
        context.ResearchRuns.Any(run => run.Id == finding.ResearchRunId && run.ArchiveId == archiveId && run.Status == "completed"));

    private IQueryable<FindingSummary> Summaries(IQueryable<Finding> findings, Guid archiveId) => findings.Select(finding =>
        new FindingSummary(finding.Id, finding.ResearchRunId!.Value, finding.Statement.Substring(0, Math.Min(10000, finding.Statement.Length)),
            finding.Statement.Length > 10000, finding.FindingType, finding.Status, finding.Confidence, finding.FirstObservedAt,
            context.FindingEvidence.Count(item => item.FindingId == finding.Id
                && context.ArchiveDocumentVersions.Any(grant => grant.ArchiveId == archiveId && grant.DocumentVersionId == item.DocumentVersionId))));
}
