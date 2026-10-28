using System.Text.Json;
using OrionSearch.Domain.Jobs;
using OrionSearch.Domain.Research;

namespace OrionSearch.Application.Research;

public sealed record RunStatistics(int Queries = 0, int Sources = 0, int Versions = 0, int Findings = 0);
public sealed record ResearchStatus(Guid Id, Guid ArchiveId, string Status, DateTime CreatedAt,
    DateTime? StartedAt, DateTime? FinishedAt, RunStatistics Statistics, int Attempts,
    int MaxAttempts, DateTime? NextAttemptAt, bool Retrying, string? Error);
public sealed record ResearchPayload(string Fingerprint, AgentRequest Request);

public interface IResearchStore
{
    Task<Job?> FindByKeyAsync(string key, CancellationToken ct);
    Task<ResearchRun?> FindRunAsync(Guid id, CancellationToken ct);
    Task<ResearchStatus?> StatusAsync(Guid id, CancellationToken ct);
    void Add(ResearchRun run, Job job);
    Task SaveAsync(CancellationToken ct);
}

public sealed record ResearchLease(Guid JobId, Guid RunId, Guid ArchiveId, int Attempt, string WorkerId);

public interface IResearchQueue
{
    Task<ResearchLease?> ClaimAsync(string workerId, CancellationToken ct);
    Task<AgentRequest?> DispatchAsync(ResearchLease lease, CancellationToken ct);
    Task<bool> HeartbeatAsync(ResearchLease lease, CancellationToken ct);
    Task<bool> CompleteAsync(ResearchLease lease, AgentResult result, CancellationToken ct);
    Task<bool> FailAsync(ResearchLease lease, ResearchFailure failure, CancellationToken ct, int? retryAfterSeconds = null);
    Task<int> RecoverAsync(CancellationToken ct);
}

public sealed class ResearchWorkerOptions
{
    public bool Enabled { get; set; }
    public int LeaseSeconds { get; set; } = 120;
    public int HeartbeatSeconds { get; set; } = 30;
    public int PollSeconds { get; set; } = 5;
    public int TimeoutSeconds { get; set; } = 300;
    public int MaxAttempts { get; set; } = 3;
    public int RetryBaseSeconds { get; set; } = 10;
    public int RetryMaxSeconds { get; set; } = 300;
    public int MaxResponseBytes { get; set; } = 2097152;
    public string? AgentBaseAddress { get; set; }
    public string? AgentApiKey { get; set; }
    public bool AllowProvisionalContract { get; set; }

    public bool IsValid() => LeaseSeconds is >= 10 and <= 3600 && HeartbeatSeconds >= 1
        && HeartbeatSeconds <= LeaseSeconds / 3 && PollSeconds is >= 1 and <= 300
        && TimeoutSeconds is >= 1 and <= 3600 && MaxAttempts is >= 1 and <= 10
        && RetryBaseSeconds is >= 1 and <= 3600 && RetryMaxSeconds >= RetryBaseSeconds && RetryMaxSeconds <= 86400
        && MaxResponseBytes is >= 1024 and <= 10485760
        && (AgentBaseAddress is null || ValidAddress(AgentBaseAddress));

    public static bool ValidAddress(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0
        && (uri.Scheme == "https" || uri.IsLoopback || uri.Host.EndsWith(".railway.internal", StringComparison.OrdinalIgnoreCase));

    public int BackoffSeconds(int attempt) => (int)Math.Min(RetryMaxSeconds, RetryBaseSeconds * Math.Pow(2, Math.Clamp(attempt - 1, 0, 30)));
}
