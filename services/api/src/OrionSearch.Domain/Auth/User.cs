namespace OrionSearch.Domain.Auth;

public sealed class User
{
    public Guid Id { get; set; }
    public required string Email { get; set; }
    public string? PasswordHash { get; set; }
    public string? NormalizedEmail { get; set; }
    public bool EmailConfirmed { get; set; }
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");
    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("N");
    public int AccessFailedCount { get; set; }
    public DateTimeOffset? LockoutEnd { get; set; }
    public bool LockoutEnabled { get; set; } = true;
    public required string DisplayName { get; set; }
    public string Status { get; set; } = "active";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
