namespace OrionSearch.Domain.Research;

public sealed class ResearchRun
{
    public Guid Id { get; set; }
    public Guid ArchiveId { get; set; }
    public string Status { get; set; } = "queued";
    public string TriggerType { get; set; } = "manual";
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string Stats { get; set; } = "{}";
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
}
