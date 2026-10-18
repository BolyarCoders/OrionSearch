using System.Text.Json;

namespace OrionSearch.Application.Archives;

public sealed record ArchiveSettingsData(
    DateOnly? DateFrom, DateOnly? DateTo, string[] Languages,
    string[] PreferredDomains, string[] ExcludedDomains, JsonElement Config, DateTime UpdatedAt);

public sealed record ArchiveSummary(
    Guid Id, string Title, string? Description, string Status, string Role,
    DateTime CreatedAt, DateTime UpdatedAt, int SourceCount);

public sealed record ArchiveDetails(ArchiveSummary Archive, ArchiveSettingsData Settings);

public sealed record SourceSummary(
    Guid DocumentId, string CanonicalUrl, string? Title, string? PublisherName, string? PublisherDomain,
    string? Language, DateTime? PublishedAt, DateTime FirstSeenAt, DateTime LastSeenAt,
    decimal? RelevanceScore, DateTime AddedAt);

public sealed record Page<T>(IReadOnlyList<T> Items, int PageNumber, int PageSize, int TotalCount);

public sealed record ArchiveListQuery(int Page = 1, int PageSize = 20, string? Status = null, bool IncludeArchived = false, string? Topic = null);

public sealed record SourceListQuery(
    int Page = 1, int PageSize = 20, DateOnly? PublishedFrom = null, DateOnly? PublishedTo = null,
    string? Language = null, string? PublisherDomain = null, string Sort = "added_desc", string? Topic = null);

public readonly record struct FieldChange<T>(bool IsSpecified, T Value);

public sealed record SettingsPatch(
    FieldChange<DateOnly?> DateFrom, FieldChange<DateOnly?> DateTo,
    FieldChange<string[]?> Languages, FieldChange<string[]?> PreferredDomains,
    FieldChange<string[]?> ExcludedDomains, FieldChange<JsonElement> Config);

public sealed record ArchivePatch(
    FieldChange<string?> Title, FieldChange<string?> Description, FieldChange<string?> Status,
    SettingsPatch? Settings);

public sealed class ArchiveRequestException(int statusCode, string message, string? field = null) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string? Field { get; } = field;
}
