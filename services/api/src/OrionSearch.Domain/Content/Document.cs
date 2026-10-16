namespace OrionSearch.Domain.Content;

public sealed class Document
{
    public Guid Id { get; set; }
    public Guid? PublisherId { get; set; }
    public required string CanonicalUrl { get; set; }
    public string? Title { get; set; }
    public string? Language { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime FirstSeenAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
