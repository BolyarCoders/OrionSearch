using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OrionSearch.Application.Archives;
using OrionSearch.Domain.Auth;
using OrionSearch.Domain.Research;
using OrionSearch.Infrastructure.Persistence;

namespace OrionSearch.Infrastructure.Archives;

internal sealed class EfArchiveStore(OrionSearchDbContext context) : IArchiveStore
{
    public Task<bool> IsActiveUserAsync(Guid userId, CancellationToken ct) =>
        context.Users.AnyAsync(user => user.Id == userId && user.Status == "active", ct);

    public async Task<IArchiveTransaction> BeginTransactionAsync(CancellationToken ct) =>
        new ArchiveTransaction(await context.Database.BeginTransactionAsync(ct));

    public async Task<ArchiveAccess?> FindAsync(Guid userId, Guid archiveId, bool forUpdate, CancellationToken ct)
    {
        // Lock before reading settings so concurrent PATCH requests merge against the latest committed state.
        var archives = forUpdate
            ? context.Archives.FromSql($"""
                SELECT a.* FROM research.archives a
                JOIN auth.archive_members m ON m.archive_id = a.id
                WHERE a.id = {archiveId} AND m.user_id = {userId}
                FOR UPDATE OF a
                """)
            : context.Archives.Where(archive => archive.Id == archiveId);
        var archive = await archives.SingleOrDefaultAsync(ct);
        if (archive is null) return null;
        var role = await context.ArchiveMembers.Where(member => member.ArchiveId == archiveId && member.UserId == userId)
            .Select(member => member.Role).SingleOrDefaultAsync(ct);
        if (role is null) return null;
        var settings = await context.ArchiveSettings.SingleOrDefaultAsync(settings => settings.ArchiveId == archiveId, ct)
            ?? throw new ArchiveRequestException(409, "Archive settings are missing.");
        var count = await context.ArchiveDocuments.CountAsync(document => document.ArchiveId == archiveId, ct);
        return new ArchiveAccess(archive, settings, role, count);
    }

    public async Task<Page<ArchiveSummary>> ListAsync(Guid userId, ArchiveListQuery query, CancellationToken ct)
    {
        var rows = from archive in context.Archives.AsNoTracking()
                   join member in context.ArchiveMembers on archive.Id equals member.ArchiveId
                   where member.UserId == userId
                       && (member.Role == "owner" || member.Role == "editor" || member.Role == "viewer")
                       && ((member.Role == "owner") == (archive.OwnerId == userId))
                   select new { Archive = archive, member.Role };
        if (query.Status is not null) rows = rows.Where(row => row.Archive.Status == query.Status);
        else if (!query.IncludeArchived) rows = rows.Where(row => row.Archive.Status != "archived");
        var count = await rows.CountAsync(ct);
        var items = await rows.OrderByDescending(row => row.Archive.CreatedAt).ThenBy(row => row.Archive.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(row => new ArchiveSummary(row.Archive.Id, row.Archive.Title, row.Archive.Description,
                row.Archive.Status, row.Role, row.Archive.CreatedAt, row.Archive.UpdatedAt,
                context.ArchiveDocuments.Count(document => document.ArchiveId == row.Archive.Id)))
            .ToListAsync(ct);
        return new Page<ArchiveSummary>(items, query.Page, query.PageSize, count);
    }

    public async Task<Page<SourceSummary>> SourcesAsync(Guid userId, Guid archiveId, SourceListQuery query, CancellationToken ct)
    {
        var rows = from link in context.ArchiveDocuments.AsNoTracking()
                   join document in context.Documents on link.DocumentId equals document.Id
                   join publisher in context.Publishers on document.PublisherId equals publisher.Id into publishers
                   from publisher in publishers.DefaultIfEmpty()
                   where link.ArchiveId == archiveId
                       && context.ArchiveMembers.Any(member => member.ArchiveId == archiveId && member.UserId == userId)
                   select new { Document = document, Publisher = publisher, Link = link };
        if (query.PublishedFrom is { } from)
        {
            var start = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            rows = rows.Where(row => row.Document.PublishedAt >= start);
        }
        if (query.PublishedTo is { } to)
        {
            if (to == DateOnly.MaxValue) rows = rows.Where(row => row.Document.PublishedAt != null);
            else
            {
                var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
                rows = rows.Where(row => row.Document.PublishedAt < end);
            }
        }
        if (query.Language is not null) rows = rows.Where(row => row.Document.Language == query.Language);
        if (query.PublisherDomain is not null) rows = rows.Where(row => row.Publisher != null && row.Publisher.Domain == query.PublisherDomain);
        var count = await rows.CountAsync(ct);
        var ordered = query.Sort switch
        {
            "added_asc" => rows.OrderBy(row => row.Link.AddedAt),
            "published_desc" => rows.OrderBy(row => row.Document.PublishedAt == null).ThenByDescending(row => row.Document.PublishedAt),
            "published_asc" => rows.OrderBy(row => row.Document.PublishedAt == null).ThenBy(row => row.Document.PublishedAt),
            "relevance_desc" => rows.OrderBy(row => row.Link.RelevanceScore == null).ThenByDescending(row => row.Link.RelevanceScore),
            "title_asc" => rows.OrderBy(row => row.Document.Title == null).ThenBy(row => row.Document.Title),
            _ => rows.OrderByDescending(row => row.Link.AddedAt)
        };
        var items = await ordered.ThenBy(row => row.Document.Id).Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(row => new SourceSummary(row.Document.Id, row.Document.CanonicalUrl, row.Document.Title,
                row.Publisher == null ? null : row.Publisher.Name, row.Publisher == null ? null : row.Publisher.Domain,
                row.Document.Language, row.Document.PublishedAt, row.Document.FirstSeenAt, row.Document.LastSeenAt,
                row.Link.RelevanceScore, row.Link.AddedAt)).ToListAsync(ct);
        return new Page<SourceSummary>(items, query.Page, query.PageSize, count);
    }

    public void Add(Archive archive, ArchiveSettings settings, Guid ownerId)
    {
        context.Archives.Add(archive);
        context.ArchiveSettings.Add(settings);
        context.ArchiveMembers.Add(new ArchiveMember { ArchiveId = archive.Id, UserId = ownerId, Role = "owner" });
    }

    public async Task SaveAsync(CancellationToken ct) => await context.SaveChangesAsync(ct);

    private sealed class ArchiveTransaction(IDbContextTransaction transaction) : IArchiveTransaction
    {
        public Task CommitAsync(CancellationToken ct) => transaction.CommitAsync(ct);
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
