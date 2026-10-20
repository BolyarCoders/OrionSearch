namespace OrionSearch.Domain.Auth;

public sealed class ArchiveMember
{
    public Guid ArchiveId { get; set; }
    public Guid UserId { get; set; }
    public string Role { get; set; } = "viewer";
    public DateTime JoinedAt { get; set; }
}
