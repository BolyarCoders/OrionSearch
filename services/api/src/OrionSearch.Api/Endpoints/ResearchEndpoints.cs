using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using OrionSearch.Application.Archives;
using OrionSearch.Application.Research;

namespace OrionSearch.Api.Endpoints;

public static class ResearchEndpoints
{
    public static IEndpointRouteBuilder MapResearchEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/archives/{archiveId:guid}/research-jobs", async (Guid archiveId, StartResearchRequest request,
            [FromHeader(Name = "Idempotency-Key")] string? key, HttpContext context, ResearchService service, CancellationToken ct) =>
        {
            ArchiveValidation.Require(context.Request.Headers["Idempotency-Key"].Count <= 1, "Idempotency-Key", "Provide only one Idempotency-Key header.");
            var result = await service.SubmitAsync(archiveId, request.Objective, key, ct);
            context.Response.Headers.CacheControl = "no-store";
            return Results.Accepted($"/research-jobs/{result.Id}", result);
        }).RequireAuthorization().RequireRateLimiting("research-submissions").WithTags("Research").WithName("StartResearch")
            .WithSummary("Queue research for an active archive without waiting for AI execution")
            .WithDescription("An active authenticated application user is required. Owner/editor membership required. An omitted objective defaults to the archive title; explicit objectives must be nonblank and at most 2000 characters. Optional Idempotency-Key: 1–200 visible ASCII characters, scoped to the archive. Matching retries return the original run; incompatible reuse returns 409. Without a key each request creates a new run. Archive settings are snapshotted by the server. The public id is research_runs.id, never the internal jobs.id. Processing and versioned agent integration require explicit server configuration.")
            .Produces<ResearchStatus>(202).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409).ProducesProblem(429);
        endpoints.MapGet("/research-jobs/{id:guid}", async (Guid id, ResearchService service, HttpContext context, CancellationToken ct) =>
        {
            var result = await service.GetAsync(id, ct);
            context.Response.Headers.CacheControl = "no-store";
            if (result.Status is "queued" or "running") context.Response.Headers.RetryAfter = "2";
            return Results.Ok(result);
        })
            .RequireAuthorization().WithTags("Research").WithName("GetResearchStatus")
            .WithSummary("Get an authorized research run's status and safe statistics")
            .WithDescription("An active authenticated application user is required. Owner/editor/viewer membership required; nonmembers and unknown runs receive 404. Status: queued, running, completed, failed, cancelled. A queued run with retrying=true is awaiting another attempt; paused archives retain pending work. Archived work cancels before dispatch or result commit. Public responses exclude queue payloads, worker identities, private model settings and exception details.")
            .Produces<ResearchStatus>().ProducesProblem(401).ProducesProblem(404).ProducesProblem(409);
        return endpoints;
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class StartResearchRequest
{
    public string? Objective { get; set; }
}
