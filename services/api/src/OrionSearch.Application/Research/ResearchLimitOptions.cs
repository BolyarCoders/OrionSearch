namespace OrionSearch.Application.Research;

public sealed class ResearchLimitOptions
{
    public int MaxOutstandingGlobal { get; set; } = 100;
    public int MaxOutstandingPerOwner { get; set; } = 10;
    public int MaxOutstandingPerArchive { get; set; } = 5;
    public int MaxRunsPerOwnerPerDay { get; set; } = 20;
    public int MaxRunsGlobalPerDay { get; set; } = 100;
    public int MaxRunsPerOwnerPerMinute { get; set; } = 5;
    public int MaxSubmissionsPerIpPerMinute { get; set; } = 20;
    public int MaxConcurrentAttempts { get; set; } = 1;
    public int MaxAttemptsPerOwnerPerDay { get; set; } = 30;
    public int MaxAttemptsGlobalPerDay { get; set; } = 100;

    public bool IsValid() => new[] { MaxOutstandingGlobal, MaxOutstandingPerOwner, MaxOutstandingPerArchive,
        MaxRunsPerOwnerPerDay, MaxRunsGlobalPerDay, MaxRunsPerOwnerPerMinute, MaxSubmissionsPerIpPerMinute,
        MaxAttemptsPerOwnerPerDay, MaxAttemptsGlobalPerDay }.All(value => value is >= 1 and <= 100000)
        && MaxConcurrentAttempts is >= 1 and <= 100;
}

public interface IResearchAdmission
{
    // Called inside the same transaction that inserts a new run/job.
    Task<DateTime> RequireCapacityAsync(Guid ownerId, Guid archiveId, CancellationToken ct);
}
