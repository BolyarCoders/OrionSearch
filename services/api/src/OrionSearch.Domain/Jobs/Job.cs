namespace OrionSearch.Domain.Jobs;

public sealed class Job
{
    public Guid Id { get; set; }
    public Guid? ResearchRunId { get; set; }
    public required string JobType { get; set; }
    public string Payload { get; set; } = "{}";
    public string Status { get; set; } = "pending";
    public int Priority { get; set; } = 0;
    public int Attempts { get; set; } = 0;
    public int MaxAttempts { get; set; } = 3;
    public DateTime RunAfter { get; set; }
    public DateTime? LockedUntil { get; set; }
    public string? WorkerId { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
