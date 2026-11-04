using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using OrionSearch.Application.Archives;

namespace OrionSearch.Api.Archives;

internal sealed class ArchiveExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not ArchiveRequestException request) return false;
        context.Response.StatusCode = request.StatusCode;
        if (request.RetryAfterSeconds is { } retry)
        {
            context.Response.Headers.RetryAfter = retry.ToString(System.Globalization.CultureInfo.InvariantCulture);
            context.Response.Headers.CacheControl = "no-store";
        }
        ProblemDetails problem = request.Field is { } field
            ? new HttpValidationProblemDetails(new Dictionary<string, string[]> { [field] = [request.Message] })
            : new ProblemDetails();
        problem.Status = request.StatusCode;
        problem.Title = request.Message;
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem });
    }
}
