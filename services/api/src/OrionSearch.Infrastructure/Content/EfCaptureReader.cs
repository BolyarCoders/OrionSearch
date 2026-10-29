using Microsoft.EntityFrameworkCore;
using OrionSearch.Application.Content;
using OrionSearch.Application.Archives;
using OrionSearch.Infrastructure.Persistence;

namespace OrionSearch.Infrastructure.Content;

internal sealed class EfCaptureReader(OrionSearchDbContext context) : ICaptureReader
{
    public async Task<Page<CapturedVersionSummary>?> ListAsync(Guid archiveId, Guid documentId, int page, int pageSize, CancellationToken ct)
    {
        if (!await context.ArchiveDocuments.AnyAsync(link => link.ArchiveId == archiveId && link.DocumentId == documentId, ct)) return null;
        var query = from grant in context.ArchiveDocumentVersions
                    join version in context.DocumentVersions on grant.DocumentVersionId equals version.Id
                    where grant.ArchiveId == archiveId && grant.DocumentId == documentId
                    select new { Version = version, grant.StorageUri };
        var count = await query.CountAsync(ct);
        var items = await query.OrderByDescending(item => item.Version.FetchedAt).ThenBy(item => item.Version.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(item => new CapturedVersionSummary(item.Version.Id, item.Version.FetchedAt, item.Version.ExtractedText != null, item.StorageUri != null))
            .ToArrayAsync(ct);
        return new Page<CapturedVersionSummary>(items, page, pageSize, count);
    }

    public async Task<StoredCapture?> FindAsync(Guid archiveId, Guid documentId, Guid versionId, CancellationToken ct)
    {
        var accepted = await (from grant in context.ArchiveDocumentVersions
                              join capture in context.DocumentVersions on grant.DocumentVersionId equals capture.Id
                              where grant.ArchiveId == archiveId && grant.DocumentId == documentId && capture.Id == versionId
                              select new { Version = capture, grant.StorageUri, grant.RawSha256 }).AsNoTracking().SingleOrDefaultAsync(ct);
        if (accepted is null) return null;
        var version = accepted.Version;
        var chunks = await context.DocumentChunks.AsNoTracking().Where(chunk => chunk.VersionId == versionId)
            .OrderBy(chunk => chunk.ChunkIndex).Select(chunk => new CaptureChunk(chunk.ChunkIndex, chunk.Content, chunk.StartOffset, chunk.EndOffset)).ToArrayAsync(ct);
        return new StoredCapture(new CapturedVersion(version.Id, documentId, version.FetchedAt, version.ContentHash,
            version.ExtractedText, accepted.StorageUri is not null, chunks), accepted.StorageUri, accepted.RawSha256);
    }
}
