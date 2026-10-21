namespace OrionSearch.Domain.Research;

public sealed class ArchiveSettings
{
    public Guid ArchiveId { get; set; }
    public DateOnly? DateFrom { get; set; }
    public DateOnly? DateTo { get; set; }
    public string[] Languages { get; set; } = [];
    public string[] PreferredDomains { get; set; } = [];
    public string[] ExcludedDomains { get; set; } = [];
    public string Config { get; set; } = "{}";
    public DateTime UpdatedAt { get; set; }
}
