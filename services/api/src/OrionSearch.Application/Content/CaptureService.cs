using OrionSearch.Application.Archives;

namespace OrionSearch.Application.Content;

public sealed record CaptureChunk(int Index, string Text, int? StartOffset, int? EndOffset);
public sealed record CapturedVersionSummary(Guid Id, DateTime FetchedAt, bool HasText, bool HasRaw);
public sealed record CapturedVersion(Guid Id, Guid DocumentId, DateTime FetchedAt, string ContentHash,
    string? ExtractedText, bool HasRaw, CaptureChunk[] Chunks);
public sealed record StoredCapture(CapturedVersion Version, string? StorageUri, string? RawSha256);

public interface ICaptureReader
{
    Task<StoredCapture?> FindAsync(Guid archiveId, Guid documentId, Guid versionId, CancellationToken ct);
    Task<Page<CapturedVersionSummary>?> ListAsync(Guid archiveId, Guid documentId, int page, int pageSize, CancellationToken ct);
}

public sealed class CaptureService(ICaptureReader reader, ICaptureStore store, ArchiveAuthorization authorization)
{
    public async Task<Page<CapturedVersionSummary>> ListAsync(Guid archiveId, Guid documentId, int page, int pageSize, CancellationToken ct)
    {
        var user = await authorization.RequireUserAsync(ct);
        await authorization.RequireAccessAsync(user, archiveId, false, ct);
        ArchiveValidation.Pagination(page, pageSize);
        return await reader.ListAsync(archiveId, documentId, page, pageSize, ct)
            ?? throw new ArchiveRequestException(404, "Source not found.");
    }

    public async Task<CapturedVersion> GetAsync(Guid archiveId, Guid documentId, Guid versionId, CancellationToken ct) =>
        (await FindAsync(archiveId, documentId, versionId, ct)).Version;

    public async Task<byte[]> DownloadAsync(Guid archiveId, Guid documentId, Guid versionId, CancellationToken ct)
    {
        var capture = await FindAsync(archiveId, documentId, versionId, ct);
        if (capture.StorageUri is null) throw new ArchiveRequestException(404, "Raw capture not found.");
        if (capture.RawSha256 is null) throw new ArchiveRequestException(503, "Raw capture is unavailable.");
        try { return (await store.ReadAsync(capture.StorageUri, capture.RawSha256, null, ct)).Bytes; }
        catch (Research.ResearchAgentException) { throw new ArchiveRequestException(503, "Raw capture is unavailable."); }
    }

    private async Task<StoredCapture> FindAsync(Guid archiveId, Guid documentId, Guid versionId, CancellationToken ct)
    {
        var user = await authorization.RequireUserAsync(ct);
        await authorization.RequireAccessAsync(user, archiveId, false, ct);
        return await reader.FindAsync(archiveId, documentId, versionId, ct)
            ?? throw new ArchiveRequestException(404, "Captured version not found.");
    }
}
