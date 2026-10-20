using OrionSearch.Domain.Research;

namespace OrionSearch.Application.Archives;

public sealed record ArchiveAccess(Archive Archive, ArchiveSettings Settings, string Role, int SourceCount);

public interface IArchiveTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}

public interface IArchiveStore
{
    Task<bool> IsActiveUserAsync(Guid userId, CancellationToken cancellationToken);
    Task<IArchiveTransaction> BeginTransactionAsync(CancellationToken cancellationToken);
    Task<ArchiveAccess?> FindAsync(Guid userId, Guid archiveId, bool forUpdate, CancellationToken cancellationToken);
    Task<Page<ArchiveSummary>> ListAsync(Guid userId, ArchiveListQuery query, CancellationToken cancellationToken);
    Task<Page<SourceSummary>> SourcesAsync(Guid userId, Guid archiveId, SourceListQuery query, CancellationToken cancellationToken);
    void Add(Archive archive, ArchiveSettings settings, Guid ownerId);
    Task SaveAsync(CancellationToken cancellationToken);
}
