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
        ProblemDetails problem = request.Field is { } field
            ? new HttpValidationProblemDetails(new Dictionary<string, string[]> { [field] = [request.Message] })
            : new ProblemDetails();
        problem.Status = request.StatusCode;
        problem.Title = request.Message;
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem });
    }
}
