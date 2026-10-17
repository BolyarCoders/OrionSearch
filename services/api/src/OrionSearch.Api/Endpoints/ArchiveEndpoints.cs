using OrionSearch.Api.Archives;
using OrionSearch.Application.Archives;

namespace OrionSearch.Api.Endpoints;

public static class ArchiveEndpoints
{
    public static IEndpointRouteBuilder MapArchiveEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/archives").WithTags("Archives").RequireAuthorization();

        group.MapPost("", async (CreateArchiveRequest request, ArchiveService service, CancellationToken ct) =>
        {
            ArchiveValidation.Require(request.Settings is not null, "settings", "Settings cannot be null.");
            var result = await service.CreateAsync(request.Title, request.Description, request.Settings!.ToSettings(), ct);
            return Results.Created($"/archives/{result.Archive.Id}", result);
        }).WithName("CreateArchive").WithSummary("Create an archive, settings and owner membership atomically")
            .WithDescription("Requires a trusted authenticated, active application user. Title: 1–200 characters; description: at most 10000. Settings default to empty arrays and config object. Dates must be ordered; ISO 639-1 languages and DNS domains must be unique after normalization, without preferred/excluded overlap. No client ownerId is accepted.")
            .Produces<ArchiveDetails>(201).ProducesProblem(400).ProducesProblem(401);

        group.MapGet("", async ([AsParameters] ArchiveListParameters query, ArchiveService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(query.ToQuery(), ct)))
            .WithName("ListArchives").WithSummary("List archives accessible through persisted membership")
            .WithDescription("Requires an authenticated active user. page defaults to 1 (maximum 1000000); pageSize defaults to 20 (maximum 100). Sort: createdAt descending, id ascending. Archived archives are hidden unless includeArchived=true or status=archived. Status supports active, paused, archived. Filtering, counts and pagination run in PostgreSQL.")
            .Produces<Page<ArchiveSummary>>().ProducesProblem(400).ProducesProblem(401);

        group.MapGet("/{id:guid}", async (Guid id, ArchiveService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(id, ct)))
            .WithName("GetArchive").WithSummary("Get archive metadata, membership role and research settings")
            .WithDescription("Owner, editor or viewer membership required. Nonmembers receive 404. Inconsistent ownership or missing settings returns 409.")
            .Produces<ArchiveDetails>().ProducesProblem(401).ProducesProblem(404).ProducesProblem(409);

        group.MapPatch("/{id:guid}", async (Guid id, PatchArchiveRequest request, ArchiveService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateAsync(id, request.ToPatch(), ct)))
            .WithName("UpdateArchive").WithSummary("Update archive and research settings in one transaction")
            .WithDescription("Owners and editors may edit metadata/settings; only owners may supply status (active, paused, archived). Omitted fields stay unchanged. Null clears description/date boundaries; null title, status, settings, arrays and config are rejected. Arrays and config replace the supplied field completely; nested config is not merged. The complete resulting settings are validated. Config is a JSON object of at most 16 KiB with opaque research preferences; no job execution is triggered.")
            .Produces<ArchiveDetails>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);

        group.MapDelete("/{id:guid}", async (Guid id, ArchiveService service, CancellationToken ct) =>
        {
            await service.ArchiveAsync(id, ct);
            return Results.NoContent();
        }).WithName("ArchiveArchive").WithSummary("Soft-archive an archive; preserve its research and documents")
            .WithDescription("Owner only. Sets status=archived; repeated requests are safe. No rows are deleted. Owner PATCH status=active unarchives it.")
            .Produces(204).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);

        group.MapGet("/{id:guid}/sources", async (Guid id, [AsParameters] SourceListParameters query, ArchiveService service, CancellationToken ct) =>
            Results.Ok(await service.SourcesAsync(id, query.ToQuery(), ct)))
            .WithName("ListArchiveSources").WithSummary("List stored sources linked to an accessible archive")
            .WithDescription("Owner, editor or viewer required. page=1, pageSize=20 (maximum 100). Publication boundaries are inclusive UTC dates and exclude unknown publication dates; no retrieval-date substitution. Language uses ISO 639-1; publisherDomain uses normalized DNS. sort: added_desc (default), added_asc, published_desc, published_asc, relevance_desc, title_asc. Null sort values come last; documentId breaks ties. Discovered documents without versions are included; extracted text and raw storage paths are omitted.")
            .Produces<Page<SourceSummary>>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409);
        return endpoints;
    }
}
