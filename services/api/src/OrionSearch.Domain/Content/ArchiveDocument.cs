namespace OrionSearch.Domain.Content;

public sealed class ArchiveDocument
{
    public Guid ArchiveId { get; set; }
    public Guid DocumentId { get; set; }
    public decimal? RelevanceScore { get; set; }
    public DateTime AddedAt { get; set; }
}
