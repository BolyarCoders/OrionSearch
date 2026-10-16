namespace OrionSearch.Domain.Content;

public sealed class Publisher
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Domain { get; set; }
    public string? HomepageUrl { get; set; }
    public DateTime CreatedAt { get; set; }
}
