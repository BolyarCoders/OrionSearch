using System.Text.Json;
using System.Text.Json.Serialization;
using OrionSearch.Application.Archives;
using OrionSearch.Domain.Research;

namespace OrionSearch.Api.Archives;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateArchiveRequest
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public CreateSettingsRequest? Settings { get; set; } = new();
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateSettingsRequest
{
    public DateOnly? DateFrom { get; set; }
    public DateOnly? DateTo { get; set; }
    public string[]? Languages { get; set; } = [];
    public string[]? PreferredDomains { get; set; } = [];
    public string[]? ExcludedDomains { get; set; } = [];
    public JsonElement Config { get; set; } = JsonSerializer.SerializeToElement(new { });

    public ArchiveSettings ToSettings() => new()
    {
        DateFrom = DateFrom,
        DateTo = DateTo,
        Languages = Languages!,
        PreferredDomains = PreferredDomains!,
        ExcludedDomains = ExcludedDomains!,
        Config = ArchiveValidation.Config(Config)
    };
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class PatchArchiveRequest
{
    public string? Title { get; set { field = value; HasTitle = true; } }
    public string? Description { get; set { field = value; HasDescription = true; } }
    public string? Status { get; set { field = value; HasStatus = true; } }
    public PatchSettingsRequest? Settings { get; set { field = value; HasSettings = true; } }
    [JsonIgnore] public bool HasTitle { get; private set; }
    [JsonIgnore] public bool HasDescription { get; private set; }
    [JsonIgnore] public bool HasStatus { get; private set; }
    [JsonIgnore] public bool HasSettings { get; private set; }

    public ArchivePatch ToPatch()
    {
        ArchiveValidation.Require(!HasSettings || Settings is not null, "settings", "Settings cannot be null.");
        return new(new(HasTitle, Title), new(HasDescription, Description), new(HasStatus, Status), Settings?.ToPatch());
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class PatchSettingsRequest
{
    public DateOnly? DateFrom { get; set { field = value; HasDateFrom = true; } }
    public DateOnly? DateTo { get; set { field = value; HasDateTo = true; } }
    public string[]? Languages { get; set { field = value; HasLanguages = true; } }
    public string[]? PreferredDomains { get; set { field = value; HasPreferredDomains = true; } }
    public string[]? ExcludedDomains { get; set { field = value; HasExcludedDomains = true; } }
    public JsonElement Config { get; set { field = value; HasConfig = true; } }
    [JsonIgnore] public bool HasDateFrom { get; private set; }
    [JsonIgnore] public bool HasDateTo { get; private set; }
    [JsonIgnore] public bool HasLanguages { get; private set; }
    [JsonIgnore] public bool HasPreferredDomains { get; private set; }
    [JsonIgnore] public bool HasExcludedDomains { get; private set; }
    [JsonIgnore] public bool HasConfig { get; private set; }

    public SettingsPatch ToPatch() => new(new(HasDateFrom, DateFrom), new(HasDateTo, DateTo),
        new(HasLanguages, Languages), new(HasPreferredDomains, PreferredDomains),
        new(HasExcludedDomains, ExcludedDomains), new(HasConfig, Config));
}

public sealed class ArchiveListParameters
{
    public int? Page { get; set; }
    public int? PageSize { get; set; }
    public string? Status { get; set; }
    public bool? IncludeArchived { get; set; }
    public ArchiveListQuery ToQuery() => new(Page ?? 1, PageSize ?? 20, Status, IncludeArchived ?? false);
}

public sealed class SourceListParameters
{
    public int? Page { get; set; }
    public int? PageSize { get; set; }
    public DateOnly? PublishedFrom { get; set; }
    public DateOnly? PublishedTo { get; set; }
    public string? Language { get; set; }
    public string? PublisherDomain { get; set; }
    public string? Sort { get; set; }
    public SourceListQuery ToQuery() => new(Page ?? 1, PageSize ?? 20, PublishedFrom, PublishedTo, Language, PublisherDomain, Sort ?? "added_desc");
}
