using System.Text.Json;
using OrionSearch.Application.Abstractions;
using OrionSearch.Domain.Research;

namespace OrionSearch.Application.Archives;

public sealed class ArchiveService(IArchiveStore store, ICurrentUser currentUser)
{
    public async Task<ArchiveDetails> CreateAsync(string? title, string? description, ArchiveSettings settings, CancellationToken ct)
    {
        var userId = await UserAsync(ct);
        var archive = new Archive { Id = Guid.NewGuid(), OwnerId = userId, Title = ArchiveValidation.Title(title), Description = ArchiveValidation.Description(description) };
        settings.ArchiveId = archive.Id;
        ArchiveValidation.Settings(settings);
        await using var transaction = await store.BeginTransactionAsync(ct);
        store.Add(archive, settings, userId);
        await store.SaveAsync(ct);
        await transaction.CommitAsync(ct);
        return Details(new ArchiveAccess(archive, settings, "owner", 0));
    }

    public async Task<Page<ArchiveSummary>> ListAsync(ArchiveListQuery query, CancellationToken ct)
    {
        var userId = await UserAsync(ct);
        ArchiveValidation.Pagination(query.Page, query.PageSize);
        if (query.Status is not null)
        {
            ArchiveValidation.Status(query.Status);
        }
        return await store.ListAsync(userId, query, ct);
    }

    public async Task<ArchiveDetails> GetAsync(Guid id, CancellationToken ct) =>
        Details(await AccessAsync(await UserAsync(ct), id, false, ct));

    public async Task<ArchiveDetails> UpdateAsync(Guid id, ArchivePatch patch, CancellationToken ct)
    {
        var userId = await UserAsync(ct);
        await using var transaction = await store.BeginTransactionAsync(ct);
        var access = await AccessAsync(userId, id, true, ct);
        if (access.Role == "viewer" || (patch.Status.IsSpecified && access.Role != "owner"))
        {
            throw new ArchiveRequestException(403, "Your membership does not permit this change.");
        }
        if (patch.Title.IsSpecified)
        {
            access.Archive.Title = ArchiveValidation.Title(patch.Title.Value);
        }
        if (patch.Description.IsSpecified)
        {
            access.Archive.Description = ArchiveValidation.Description(patch.Description.Value);
        }
        if (patch.Status.IsSpecified)
        {
            access.Archive.Status = ArchiveValidation.Status(patch.Status.Value);
        }
        if (patch.Settings is { } changes)
        {
            var settings = access.Settings;
            if (changes.DateFrom.IsSpecified) settings.DateFrom = changes.DateFrom.Value;
            if (changes.DateTo.IsSpecified) settings.DateTo = changes.DateTo.Value;
            if (changes.Languages.IsSpecified) settings.Languages = changes.Languages.Value!;
            if (changes.PreferredDomains.IsSpecified) settings.PreferredDomains = changes.PreferredDomains.Value!;
            if (changes.ExcludedDomains.IsSpecified) settings.ExcludedDomains = changes.ExcludedDomains.Value!;
            if (changes.Config.IsSpecified) settings.Config = ArchiveValidation.Config(changes.Config.Value);
            ArchiveValidation.Settings(settings);
        }
        await store.SaveAsync(ct);
        await transaction.CommitAsync(ct);
        return Details(access);
    }

    public async Task ArchiveAsync(Guid id, CancellationToken ct)
    {
        var userId = await UserAsync(ct);
        await using var transaction = await store.BeginTransactionAsync(ct);
        var access = await AccessAsync(userId, id, true, ct);
        if (access.Role != "owner")
        {
            throw new ArchiveRequestException(403, "Only the owner can archive an archive.");
        }
        access.Archive.Status = "archived";
        await store.SaveAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<Page<SourceSummary>> SourcesAsync(Guid id, SourceListQuery query, CancellationToken ct)
    {
        var userId = await UserAsync(ct);
        await AccessAsync(userId, id, false, ct);
        ArchiveValidation.Pagination(query.Page, query.PageSize);
        ArchiveValidation.Require(query.PublishedFrom is null || query.PublishedTo is null || query.PublishedFrom <= query.PublishedTo,
            "publishedFrom", "Published from must be on or before published to.");
        ArchiveValidation.Require(query.Sort is "added_desc" or "added_asc" or "published_desc" or "published_asc" or "relevance_desc" or "title_asc",
            "sort", "Use added_desc, added_asc, published_desc, published_asc, relevance_desc or title_asc.");
        query = query with
        {
            Language = query.Language is null ? null : ArchiveValidation.Language(query.Language),
            PublisherDomain = query.PublisherDomain is null ? null : ArchiveValidation.Domain(query.PublisherDomain)
        };
        return await store.SourcesAsync(userId, id, query, ct);
    }

    private async Task<Guid> UserAsync(CancellationToken ct)
    {
        if (currentUser.UserId is not { } id || !await store.IsActiveUserAsync(id, ct))
        {
            throw new ArchiveRequestException(401, "A valid authenticated application user is required.");
        }
        return id;
    }

    private async Task<ArchiveAccess> AccessAsync(Guid userId, Guid id, bool forUpdate, CancellationToken ct)
    {
        var access = await store.FindAsync(userId, id, forUpdate, ct)
            ?? throw new ArchiveRequestException(404, "Archive not found.");
        if (access.Role is not ("owner" or "editor" or "viewer"))
        {
            throw new ArchiveRequestException(404, "Archive not found.");
        }
        if ((access.Role == "owner") != (access.Archive.OwnerId == userId))
        {
            throw new ArchiveRequestException(409, "Archive ownership and membership are inconsistent.");
        }
        return access;
    }

    private static ArchiveDetails Details(ArchiveAccess access)
    {
        var archive = access.Archive;
        var settings = access.Settings;
        return new ArchiveDetails(new ArchiveSummary(archive.Id, archive.Title, archive.Description, archive.Status,
            access.Role, archive.CreatedAt, archive.UpdatedAt, access.SourceCount),
            new ArchiveSettingsData(settings.DateFrom, settings.DateTo, settings.Languages, settings.PreferredDomains,
                settings.ExcludedDomains, JsonSerializer.Deserialize<JsonElement>(settings.Config), settings.UpdatedAt));
    }
}
