namespace OrionSearch.Domain.Research;

public sealed class ResearchQuery
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public required string Query { get; set; }
    public required string Provider { get; set; }
    public string Status { get; set; } = "queued";
    public DateTime CreatedAt { get; set; }
}
