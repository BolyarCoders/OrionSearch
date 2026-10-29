using OrionSearch.Application.Archives;
using OrionSearch.Application.Research;

namespace OrionSearch.Api.Endpoints;

public static class ResearchResultsEndpoints
{
    public static IEndpointRouteBuilder MapResearchResultsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/archives/{archiveId:guid}").RequireAuthorization().WithTags("Research results");
        group.MapGet("/research-jobs", async (Guid archiveId, int? page, int? pageSize, string? status,
            ResearchResultsService service, HttpContext context, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(await service.HistoryAsync(archiveId, new(page ?? 1, pageSize ?? 20, status), ct));
        }).WithName("ListResearchHistory").WithSummary("List safe research history for an accessible archive")
            .WithDescription("Owner/editor/viewer membership and active user required. page=1, pageSize=20 (max 100). Optional status: queued, running, completed, failed, cancelled. Filter/count before paging; createdAt descending, id ascending. No queue payloads or worker identities. History does not dispatch jobs.")
            .Produces<Page<ResearchStatus>>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409);
        group.MapGet("/findings", async (Guid archiveId, int? page, int? pageSize, string? status, string? type, Guid? runId,
            ResearchResultsService service, HttpContext context, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(await service.FindingsAsync(archiveId, new(page ?? 1, pageSize ?? 20, status, type, runId), ct));
        }).WithName("ListArchiveFindings").WithSummary("List findings from completed runs belonging to this archive")
            .WithDescription("Owner/editor/viewer and active user required. page=1, pageSize=20 (max 100). Optional status: unverified, supported, disputed, superseded; type: claim, observation, estimate; runId must belong to this archive or returns 404. firstObservedAt descending, id ascending. Legacy findings without run provenance are hidden; evidenceCount includes only exact granted versions. Statements over 10000 characters are truncated with an explicit flag. AI confidence is not a verification verdict.")
            .Produces<Page<FindingSummary>>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409);
        group.MapGet("/findings/{findingId:guid}", async (Guid archiveId, Guid findingId, int? page, int? pageSize,
            ResearchResultsService service, HttpContext context, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(await service.FindingAsync(archiveId, findingId, page ?? 1, pageSize ?? 20, ct));
        }).WithName("GetArchiveFinding").WithSummary("Read a finding with a page of authorized evidence references")
            .WithDescription("Requires active user, archive membership and completed-run provenance. page/pageSize paginate evidence (defaults 1/20, max 100), ordered by evidence id. Evidence additionally requires the archive's version grant. Quote limit 10000, title limit 2000; quoteTruncated distinguishes legacy oversized quotes. Document/version/chunk IDs allow navigation to the P3 text/raw endpoints. No storage URI, unrelated archive content or queue data is exposed.")
            .Produces<FindingDetails>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409);
        group.MapGet("/sources/{documentId:guid}", async (Guid archiveId, Guid documentId, ResearchResultsService service,
            HttpContext context, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(await service.SourceAsync(archiveId, documentId, ct));
        }).WithName("GetArchiveSource").WithSummary("Read source metadata and this archive's accepted version count")
            .WithDescription("Requires active user and owner/editor/viewer membership. A global document alone grants no access: archive_document link required. versionCount uses explicit archive/version grants; follow the paginated versions route for accepted IDs. Discovery-only sources have zero versions.")
            .Produces<SourceDetails>().ProducesProblem(401).ProducesProblem(404).ProducesProblem(409);
        return endpoints;
    }
}
