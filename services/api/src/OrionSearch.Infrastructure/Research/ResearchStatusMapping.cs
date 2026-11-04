using System.Text.Json;
using OrionSearch.Application.Research;
using OrionSearch.Domain.Jobs;
using OrionSearch.Domain.Research;

namespace OrionSearch.Infrastructure.Research;

internal static class ResearchStatusMapping
{
    public static ResearchStatus Map(ResearchRun run, Job? job)
    {
        var statistics = new RunStatistics();
        try
        {
            using var json = JsonDocument.Parse(run.Stats);
            int Count(string name) => json.RootElement.ValueKind == JsonValueKind.Object
                && json.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
                && value.TryGetInt32(out var count) && count >= 0 ? count : 0;
            statistics = new RunStatistics(Count("queries"), Count("sources"), Count("versions"), Count("findings"));
        }
        catch (JsonException) { }
        var next = job?.Status == "pending" ? job.RunAfter : (DateTime?)null;
        return new ResearchStatus(run.Id, run.ArchiveId, run.Status, run.CreatedAt, run.StartedAt, run.FinishedAt,
            statistics, job?.Attempts ?? 0, job?.MaxAttempts ?? 0, next,
            run.Status == "queued" && job?.Attempts > 0, ResearchFailures.Sanitize(run.ErrorMessage));
    }
}
