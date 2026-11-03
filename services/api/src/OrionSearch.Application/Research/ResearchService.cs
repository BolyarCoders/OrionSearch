using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OrionSearch.Application.Archives;
using OrionSearch.Domain.Jobs;
using OrionSearch.Domain.Research;

namespace OrionSearch.Application.Research;

public sealed class ResearchService(IArchiveStore archives, ArchiveAuthorization authorization, IResearchStore store, ResearchWorkerOptions options, IResearchAdmission admission)
{
    public async Task<ResearchStatus> SubmitAsync(Guid archiveId, string? objective, string? clientKey, CancellationToken ct)
    {
        var userId = await authorization.RequireUserAsync(ct);
        ArchiveValidation.Require(objective is null || (!string.IsNullOrWhiteSpace(objective) && objective.Length <= 2000),
            "objective", "Objective must be nonblank and at most 2000 characters when supplied.");
        ArchiveValidation.Require(clientKey is null || (clientKey.Length is >= 1 and <= 200 && clientKey.All(c => c is >= '!' and <= '~')),
            "Idempotency-Key", "Idempotency-Key must contain 1–200 visible ASCII characters.");
        objective = objective?.Trim();
        var fingerprint = Hash(JsonSerializer.Serialize(new { objective }));
        var key = clientKey is null ? null : Hash($"orion:research:{archiveId:N}:{clientKey}");
        await using var transaction = await archives.BeginTransactionAsync(ct);
        var access = await authorization.RequireAccessAsync(userId, archiveId, true, ct);
        if (access.Role == "viewer") throw new ArchiveRequestException(403, "Viewers cannot start research.");
        if (key is not null && await store.FindByKeyAsync(key, ct) is { } previous)
        {
            var payload = JsonSerializer.Deserialize<ResearchPayload>(previous.Payload, ResearchContract.Json)!;
            if (payload.Fingerprint != fingerprint) throw new ArchiveRequestException(409, "Idempotency-Key was already used for a different request.");
            var existing = await store.StatusAsync(previous.ResearchRunId!.Value, ct);
            await transaction.CommitAsync(ct);
            return existing!;
        }
        if (access.Archive.Status != "active") throw new ArchiveRequestException(409, "Research requires an active archive.");
        var admittedAt = await admission.RequireCapacityAsync(access.Archive.OwnerId, archiveId, ct);
        var settings = access.Settings;
        ArchiveValidation.Settings(settings);
        var run = new ResearchRun { Id = Guid.NewGuid(), ArchiveId = archiveId, CreatedAt = admittedAt };
        var request = new AgentRequest(ResearchContract.Version, run.Id, archiveId, access.Archive.Title, access.Archive.Description,
            objective ?? access.Archive.Title, new ArchiveSettingsData(settings.DateFrom, settings.DateTo, settings.Languages,
                settings.PreferredDomains, settings.ExcludedDomains, JsonSerializer.Deserialize<JsonElement>(settings.Config), settings.UpdatedAt));
        store.Add(run, new Job
        {
            Id = Guid.NewGuid(),
            ResearchRunId = run.Id,
            JobType = "research_plan",
            CreatedAt = admittedAt,
            MaxAttempts = options.MaxAttempts,
            Payload = JsonSerializer.Serialize(new ResearchPayload(fingerprint, request), ResearchContract.Json),
            IdempotencyKey = key
        });
        await store.SaveAsync(ct);
        await transaction.CommitAsync(ct);
        return (await store.StatusAsync(run.Id, ct))!;
    }

    public async Task<ResearchStatus> GetAsync(Guid id, CancellationToken ct)
    {
        var userId = await authorization.RequireUserAsync(ct);
        var run = await store.FindRunAsync(id, ct) ?? throw new ArchiveRequestException(404, "Research run not found.");
        await authorization.RequireAccessAsync(userId, run.ArchiveId, false, ct);
        return (await store.StatusAsync(id, ct))!;
    }

    public async Task<ResearchReport> GetReportAsync(Guid id, CancellationToken ct)
    {
        var userId = await authorization.RequireUserAsync(ct);
        var run = await store.FindRunAsync(id, ct) ?? throw new ArchiveRequestException(404, "Research run not found.");
        await authorization.RequireAccessAsync(userId, run.ArchiveId, false, ct);
        if (run.Status != "completed" || run.Report is null)
            throw new ArchiveRequestException(404, "Research report not available.");
        return new ResearchReport(run.Id, run.Report);
    }

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
