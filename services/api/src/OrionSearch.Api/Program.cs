using OrionSearch.Api.Endpoints;
using OrionSearch.Api.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddJsonConsole();
builder.Services.AddApiServices(builder.Configuration, builder.Environment);

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/auth") || context.Request.Path == "/me")
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
    }
    long? maximumBodyBytes = context.Request.Path.StartsWithSegments("/auth") ? 32768
        : HttpMethods.IsPost(context.Request.Method) && context.Request.Path.StartsWithSegments("/archives")
            && context.Request.Path.Value!.EndsWith("/research-jobs", StringComparison.Ordinal) ? 16384 : null;
    if (maximumBodyBytes is { } maximum)
    {
        if (context.Request.ContentLength > maximum)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }
        if (context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
            limit.MaxRequestBodySize = maximum;
    }
    await next();
});
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthEndpoints();
app.MapAccountEndpoints();
app.MapArchiveEndpoints();
app.MapResearchEndpoints();
app.MapResearchResultsEndpoints();
app.MapCaptureEndpoints();
app.Run();

public partial class Program;
