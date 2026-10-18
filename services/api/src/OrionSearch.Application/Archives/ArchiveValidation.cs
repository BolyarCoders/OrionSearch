using System.Globalization;
using System.Text;
using System.Text.Json;
using OrionSearch.Domain.Content;
using OrionSearch.Domain.Research;

namespace OrionSearch.Application.Archives;

public static class ArchiveValidation
{
    private static readonly HashSet<string> Languages = CultureInfo.GetCultures(CultureTypes.NeutralCultures)
        .Select(culture => culture.TwoLetterISOLanguageName)
        .Where(code => code.Length == 2 && code.All(char.IsAsciiLetterLower)).ToHashSet(StringComparer.Ordinal);

    public static void Require(bool condition, string field, string message)
    {
        if (!condition)
        {
            throw new ArchiveRequestException(400, message, field);
        }
    }

    public static string Title(string? value)
    {
        Require(!string.IsNullOrWhiteSpace(value) && value.Trim().Length <= 200,
            "title", "Title must contain 1–200 non-whitespace characters.");
        return value!.Trim();
    }

    public static string? Description(string? value)
    {
        Require(value is null || value.Length <= 10000, "description", "Description cannot exceed 10000 characters.");
        return value;
    }

    public static string Status(string? value)
    {
        Require(value is "active" or "paused" or "archived", "status", "Status must be active, paused or archived.");
        return value!;
    }

    public static string? Topic(string? value)
    {
        if (value is null) return null;
        var topic = value.Trim();
        Require(topic.Length is >= 1 and <= 200 && !topic.Any(char.IsControl),
            "topic", "Topic must contain 1–200 characters after trimming, without control characters.");
        return topic;
    }

    public static void Pagination(int page, int pageSize)
    {
        Require(page is >= 1 and <= 1000000, "page", "Page must be between 1 and 1000000.");
        Require(pageSize is >= 1 and <= 100, "pageSize", "Page size must be between 1 and 100.");
    }

    public static string Language(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? "";
        Require(Languages.Contains(normalized), "languages", "Use ISO 639-1 language codes such as en or bg.");
        return normalized;
    }

    public static string Domain(string? value)
    {
        try
        {
            Require(value is { Length: <= 253 }, "domains", "A domain cannot exceed 253 characters.");
            return DomainName.Normalize(value!);
        }
        catch (ArgumentException)
        {
            throw new ArchiveRequestException(400, "Use DNS domains without a scheme, port or path.", "domains");
        }
    }

    public static void Settings(ArchiveSettings settings)
    {
        Require(settings.DateFrom is null || settings.DateTo is null || settings.DateFrom <= settings.DateTo,
            "dateFrom", "Date from must be on or before date to.");
        settings.Languages = Normalize(settings.Languages, Language, "languages");
        settings.PreferredDomains = Normalize(settings.PreferredDomains, Domain, "preferredDomains");
        settings.ExcludedDomains = Normalize(settings.ExcludedDomains, Domain, "excludedDomains");
        Require(!settings.PreferredDomains.Intersect(settings.ExcludedDomains).Any(),
            "preferredDomains", "A domain cannot be both preferred and excluded.");
        using var config = JsonDocument.Parse(settings.Config);
        Require(config.RootElement.ValueKind == JsonValueKind.Object && Encoding.UTF8.GetByteCount(settings.Config) <= 16384,
            "config", "Research config must be a JSON object of at most 16384 UTF-8 bytes.");
    }

    public static string Config(JsonElement value)
    {
        Require(value.ValueKind == JsonValueKind.Object, "config", "Research config must be a JSON object.");
        return value.GetRawText();
    }

    private static string[] Normalize(string[]? values, Func<string?, string> normalize, string field)
    {
        Require(values is { Length: <= 100 }, field, "Provide an array with at most 100 entries; null is not allowed.");
        var normalized = values!.Select(normalize).ToArray();
        Require(normalized.Distinct(StringComparer.Ordinal).Count() == normalized.Length,
            field, "Duplicate entries are not allowed, including entries equal after normalization.");
        return normalized;
    }
}
