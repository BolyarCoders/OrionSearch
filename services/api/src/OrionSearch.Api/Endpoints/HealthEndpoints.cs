namespace OrionSearch.Api.Endpoints;

public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health", () => TypedResults.Ok(new HealthResponse("healthy")))
            .WithName("GetHealth")
            .WithSummary("Reports whether the API is running.")
            .WithTags("Health")
            .AllowAnonymous();
    }

    public sealed record HealthResponse(string Status);
}
