namespace OrionSearch.Domain.Knowledge;

public sealed class FindingEvidence
{
    public Guid Id { get; set; }
    public Guid FindingId { get; set; }
    public Guid DocumentVersionId { get; set; }
    public Guid? ChunkId { get; set; }
    public string? EvidenceText { get; set; }
    public string Stance { get; set; } = "mentions";
    public DateTime CreatedAt { get; set; }
}
