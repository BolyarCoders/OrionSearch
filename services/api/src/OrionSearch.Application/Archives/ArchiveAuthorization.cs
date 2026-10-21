using OrionSearch.Application.Abstractions;

namespace OrionSearch.Application.Archives;

public sealed class ArchiveAuthorization(IArchiveStore store, ICurrentUser currentUser)
{
    public async Task<Guid> RequireUserAsync(CancellationToken ct)
    {
        if (currentUser.UserId is not { } id || !await store.IsActiveUserAsync(id, ct))
            throw new ArchiveRequestException(401, "A valid authenticated application user is required.");
        return id;
    }

    public async Task<ArchiveAccess> RequireAccessAsync(Guid userId, Guid archiveId, bool forUpdate, CancellationToken ct)
    {
        var access = await store.FindAsync(userId, archiveId, forUpdate, ct)
            ?? throw new ArchiveRequestException(404, "Archive not found.");
        if (access.Role is not ("owner" or "editor" or "viewer"))
            throw new ArchiveRequestException(404, "Archive not found.");
        if ((access.Role == "owner") != (access.Archive.OwnerId == userId))
            throw new ArchiveRequestException(409, "Archive ownership and membership are inconsistent.");
        return access;
    }
}
