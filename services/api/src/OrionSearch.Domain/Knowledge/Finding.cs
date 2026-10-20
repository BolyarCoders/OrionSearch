namespace OrionSearch.Domain.Knowledge;

public sealed class Finding
{
    public Guid Id { get; set; }
    public Guid? ResearchRunId { get; set; }
    public required string Statement { get; set; }
    public string FindingType { get; set; } = "claim";
    public string Status { get; set; } = "unverified";
    public decimal? Confidence { get; set; }
    public DateTime FirstObservedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
