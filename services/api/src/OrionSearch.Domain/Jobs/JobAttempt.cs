namespace OrionSearch.Domain.Jobs;

public sealed class JobAttempt
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public int AttemptNumber { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string Status { get; set; } = "running";
    public string? ErrorMessage { get; set; }
}
