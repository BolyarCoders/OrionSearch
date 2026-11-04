using System.Text.Json;
using System.Text.Json.Serialization;
using OrionSearch.Application.Archives;

namespace OrionSearch.Application.Research;

public static class ResearchContract
{
    public const string Version = "orion.research.v1";
    public const string ProvisionalVersion = "orion.research.provisional.v1";
    public static bool IsSupported(string? version, bool allowProvisional = false) =>
        version == Version || allowProvisional && version == ProvisionalVersion;
    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web)
    {
        RespectRequiredConstructorParameters = true,
        AllowDuplicateProperties = false,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 64
    };
}

public sealed record AgentRequest(string ContractVersion, Guid ResearchRunId, Guid ArchiveId,
    string Title, string? Description, string Objective, ArchiveSettingsData Settings,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool IncludeReport = false);
public sealed record AgentResult(string ContractVersion, Guid ResearchRunId,
    QueryOutput[] Queries, SourceOutput[] Sources, FindingOutput[] Findings,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Report = null);
public sealed record QueryOutput(string Query, string Provider);
public sealed record PublisherOutput(string Name, string Domain);
public sealed record SourceOutput(string Key, string CanonicalUrl, string? Title = null, string? Language = null,
    DateTime? PublishedAt = null, PublisherOutput? Publisher = null, decimal? Relevance = null, CaptureOutput? Capture = null);
public sealed record CaptureOutput(DateTime FetchedAt, string? ExtractedText = null, RawCaptureOutput? Raw = null,
    ChunkOutput[]? Chunks = null, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] JsonElement Metadata = default);
public sealed record RawCaptureOutput(string StorageUri, string Sha256);
public sealed record ChunkOutput(int Index, string Text, int? StartOffset = null, int? EndOffset = null, int? TokenCount = null);
public sealed record FindingOutput(string Key, string Statement, string Type, EvidenceOutput[] Evidence, decimal? Confidence = null);
public sealed record EvidenceOutput(string SourceKey, string Stance, int? ChunkIndex = null, string? Quote = null);

public interface IResearchAgentClient
{
    bool IsConfigured { get; }
    Task<AgentResult> ExecuteAsync(AgentRequest request, CancellationToken cancellationToken);
}

public enum ResearchFailure { Timeout, Unavailable, Authentication, Contract, Persistence, Paused, Archived }

public sealed class ResearchAgentException(ResearchFailure failure, int? retryAfterSeconds = null) : Exception("Research agent execution failed.")
{
    public ResearchFailure Failure { get; } = failure;
    public int? RetryAfterSeconds { get; } = retryAfterSeconds;
}

public static class ResearchFailures
{
    public static bool IsTransient(ResearchFailure failure) => failure is ResearchFailure.Timeout or ResearchFailure.Unavailable or ResearchFailure.Paused;
    public static string Summary(ResearchFailure failure) => failure switch
    {
        ResearchFailure.Timeout => "The research service timed out.",
        ResearchFailure.Unavailable => "The research service is temporarily unavailable.",
        ResearchFailure.Authentication => "The research service authentication is not configured correctly.",
        ResearchFailure.Contract => "The research service returned an invalid result.",
        ResearchFailure.Paused => "The archive was paused before dispatch.",
        ResearchFailure.Archived => "The archive was archived; research was cancelled.",
        _ => "Research results could not be stored."
    };
    public static string? Sanitize(string? value) => value is null ? null :
        Enum.GetValues<ResearchFailure>().Select(Summary).Contains(value) ? value : "Research did not complete.";
}
