using OrionSearch.Application.Content;
using OrionSearch.Application.Archives;

namespace OrionSearch.Api.Endpoints;

public static class CaptureEndpoints
{
    public static IEndpointRouteBuilder MapCaptureEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/archives/{archiveId:guid}/sources/{documentId:guid}/versions",
            async (Guid archiveId, Guid documentId, int? page, int? pageSize, CaptureService service, HttpContext context, CancellationToken ct) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                return Results.Ok(await service.ListAsync(archiveId, documentId, page ?? 1, pageSize ?? 20, ct));
            }).WithTags("Captures").RequireAuthorization().WithName("ListCapturedVersions")
            .WithSummary("List versions accepted into an accessible archive")
            .WithDescription("Defaults: page=1, pageSize=20 (maximum 100). FetchedAt descending, id ascending. A discovery without captures returns an empty page. Nonmembers and unlinked documents return 404. No global versions or raw storage references are exposed.")
            .Produces<Page<CapturedVersionSummary>>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409);
        var group = endpoints.MapGroup("/archives/{archiveId:guid}/sources/{documentId:guid}/versions/{versionId:guid}")
            .WithTags("Captures").RequireAuthorization();
        group.MapGet("", async (Guid archiveId, Guid documentId, Guid versionId, CaptureService service, HttpContext context, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(await service.GetAsync(archiveId, documentId, versionId, ct));
        }).WithName("GetCapturedVersion").WithSummary("Read text and chunks accepted into this archive")
            .WithDescription("Requires active user and owner/editor/viewer membership plus an explicit archive/version grant. Global document reuse does not grant other archives' versions. Storage references and credentials are omitted. Chunk offsets count Unicode scalar values in normalized text.")
            .Produces<CapturedVersion>().ProducesProblem(401).ProducesProblem(404).ProducesProblem(409);
        group.MapGet("/raw", async (Guid archiveId, Guid documentId, Guid versionId, CaptureService service, HttpContext context, CancellationToken ct) =>
        {
            var bytes = await service.DownloadAsync(archiveId, documentId, versionId, ct);
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'";
            return Results.File(bytes, "application/octet-stream", $"capture-{versionId:D}.bin");
        }).WithName("DownloadRawCapture").WithSummary("Download verified raw bytes as an attachment")
            .WithDescription("Rechecks the immutable object version and SHA-256 before serving any bytes. Never serves source HTML inline. Nonmembers/missing version return 404; unavailable or corrupt storage returns generic 503.")
            .Produces(200, contentType: "application/octet-stream").ProducesProblem(401).ProducesProblem(404).ProducesProblem(409).ProducesProblem(503);
        return endpoints;
    }
}
