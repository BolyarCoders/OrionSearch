namespace OrionSearch.Domain.Content;

public sealed class ArchiveDocumentVersion
{
    public Guid ArchiveId { get; set; }
    public Guid DocumentId { get; set; }
    public Guid DocumentVersionId { get; set; }
    public string? StorageUri { get; set; }
    public string? RawSha256 { get; set; }
}
