namespace OrionSearch.Domain.Content;

public sealed class DocumentVersion
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public required string ContentHash { get; set; }
    public string? ExtractedText { get; set; }
    public DateTime FetchedAt { get; set; }
    public string? StorageUri { get; set; }
    public string Metadata { get; set; } = "{}";
}
