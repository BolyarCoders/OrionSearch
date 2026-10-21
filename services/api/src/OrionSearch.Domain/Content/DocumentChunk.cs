namespace OrionSearch.Domain.Content;

public sealed class DocumentChunk
{
    public Guid Id { get; set; }
    public Guid VersionId { get; set; }
    public int ChunkIndex { get; set; }
    public required string Content { get; set; }
    public int? TokenCount { get; set; }
    public int? StartOffset { get; set; }
    public int? EndOffset { get; set; }
}
