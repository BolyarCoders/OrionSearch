using OrionSearch.Application.Archives;

namespace OrionSearch.Application.Research;

public sealed record ResearchHistoryQuery(int Page = 1, int PageSize = 20, string? Status = null);
public sealed record FindingListQuery(int Page = 1, int PageSize = 20, string? Status = null, string? Type = null, Guid? RunId = null);
public sealed record FindingSummary(Guid Id, Guid ResearchRunId, string Statement, bool StatementTruncated,
    string Type, string Status, decimal? Confidence, DateTime FirstObservedAt, int EvidenceCount);
public sealed record EvidenceReference(Guid Id, Guid DocumentId, Guid VersionId, Guid? ChunkId, int? ChunkIndex,
    string Stance, string? Quote, bool QuoteTruncated, string CanonicalUrl, string? SourceTitle);
public sealed record FindingDetails(FindingSummary Finding, Page<EvidenceReference> Evidence);
public sealed record SourceDetails(SourceSummary Source, int VersionCount);

public interface IResearchResultsStore
{
    Task<Page<ResearchStatus>> HistoryAsync(Guid archiveId, ResearchHistoryQuery query, CancellationToken ct);
    Task<bool> HasRunAsync(Guid archiveId, Guid runId, CancellationToken ct);
    Task<Page<FindingSummary>> FindingsAsync(Guid archiveId, FindingListQuery query, CancellationToken ct);
    Task<FindingDetails?> FindingAsync(Guid archiveId, Guid findingId, int page, int pageSize, CancellationToken ct);
    Task<SourceDetails?> SourceAsync(Guid archiveId, Guid documentId, CancellationToken ct);
}

public sealed class ResearchResultsService(IResearchResultsStore store, ArchiveAuthorization authorization)
{
    public async Task<Page<ResearchStatus>> HistoryAsync(Guid archiveId, ResearchHistoryQuery query, CancellationToken ct)
    {
        await AuthorizeAsync(archiveId, ct);
        ArchiveValidation.Pagination(query.Page, query.PageSize);
        ArchiveValidation.Require(query.Status is null or "queued" or "running" or "completed" or "failed" or "cancelled", "status", "Invalid research status.");
        return await store.HistoryAsync(archiveId, query, ct);
    }

    public async Task<Page<FindingSummary>> FindingsAsync(Guid archiveId, FindingListQuery query, CancellationToken ct)
    {
        await AuthorizeAsync(archiveId, ct);
        ArchiveValidation.Pagination(query.Page, query.PageSize);
        ArchiveValidation.Require(query.Status is null or "unverified" or "supported" or "disputed" or "superseded", "status", "Invalid finding status.");
        ArchiveValidation.Require(query.Type is null or "claim" or "observation" or "estimate", "type", "Invalid finding type.");
        if (query.RunId is { } run && !await store.HasRunAsync(archiveId, run, ct))
            throw new ArchiveRequestException(404, "Research run not found.");
        return await store.FindingsAsync(archiveId, query, ct);
    }

    public async Task<FindingDetails> FindingAsync(Guid archiveId, Guid findingId, int page, int pageSize, CancellationToken ct)
    {
        await AuthorizeAsync(archiveId, ct);
        ArchiveValidation.Pagination(page, pageSize);
        return await store.FindingAsync(archiveId, findingId, page, pageSize, ct)
            ?? throw new ArchiveRequestException(404, "Finding not found.");
    }

    public async Task<SourceDetails> SourceAsync(Guid archiveId, Guid documentId, CancellationToken ct)
    {
        await AuthorizeAsync(archiveId, ct);
        return await store.SourceAsync(archiveId, documentId, ct)
            ?? throw new ArchiveRequestException(404, "Source not found.");
    }

    private async Task AuthorizeAsync(Guid archiveId, CancellationToken ct)
    {
        var user = await authorization.RequireUserAsync(ct);
        await authorization.RequireAccessAsync(user, archiveId, false, ct);
    }
}
