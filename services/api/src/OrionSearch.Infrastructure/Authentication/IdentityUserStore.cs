using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrionSearch.Domain.Auth;
using OrionSearch.Infrastructure.Persistence;

namespace OrionSearch.Infrastructure.Authentication;

// Identity owns password hashing and account policy; this adapter keeps the existing user IDs and archive foreign keys.
public sealed class IdentityUserStore(OrionSearchDbContext db) :
    IUserPasswordStore<User>, IUserEmailStore<User>, IUserSecurityStampStore<User>, IUserLockoutStore<User>
{
    public void Dispose() { } // The request scope owns the context.

    public async Task<IdentityResult> CreateAsync(User user, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (user.Id == Guid.Empty) user.Id = Guid.NewGuid();
        db.Users.Add(user);
        return await SaveAsync(user, ct);
    }

    public async Task<IdentityResult> UpdateAsync(User user, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        db.Attach(user);
        user.ConcurrencyStamp = Guid.NewGuid().ToString("N");
        db.Entry(user).State = EntityState.Modified;
        return await SaveAsync(user, ct);
    }

    public Task<IdentityResult> DeleteAsync(User user, CancellationToken ct) =>
        throw new NotSupportedException("Accounts must be disabled; deletion requires a reviewed retention policy.");

    private async Task<IdentityResult> SaveAsync(User user, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return IdentityResult.Success;
        }
        catch (DbUpdateConcurrencyException)
        {
            db.Entry(user).State = EntityState.Detached;
            return IdentityResult.Failed(new IdentityErrorDescriber().ConcurrencyFailure());
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ux_users_email" or "ux_users_normalized_email" })
        {
            db.Entry(user).State = EntityState.Detached;
            return IdentityResult.Failed(new IdentityError { Code = "DuplicateAccount", Description = "Unable to register this account." });
        }
    }

    public Task<User?> FindByIdAsync(string userId, CancellationToken ct) => Guid.TryParse(userId, out var id)
        ? db.Users.SingleOrDefaultAsync(user => user.Id == id, ct) : Task.FromResult<User?>(null);
    public Task<User?> FindByNameAsync(string normalizedUserName, CancellationToken ct) => FindByEmailAsync(normalizedUserName, ct);
    public Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken ct) => db.Users.SingleOrDefaultAsync(
        user => user.NormalizedEmail == normalizedEmail || (user.NormalizedEmail == null && user.Email == normalizedEmail), ct);

    // Email is the login name. There is no separately editable username or duplicate credential table.
    public Task<string> GetUserIdAsync(User user, CancellationToken ct) => Value(user.Id.ToString(), ct);
    public Task<string?> GetUserNameAsync(User user, CancellationToken ct) => Value<string?>(user.Email, ct);
    public Task SetUserNameAsync(User user, string? name, CancellationToken ct) => Change(() => user.Email = name!, ct);
    public Task<string?> GetNormalizedUserNameAsync(User user, CancellationToken ct) => Value(user.NormalizedEmail, ct);
    public Task SetNormalizedUserNameAsync(User user, string? name, CancellationToken ct) => Change(() => user.NormalizedEmail = name, ct);
    public Task SetPasswordHashAsync(User user, string? hash, CancellationToken ct) => Change(() => user.PasswordHash = hash, ct);
    public Task<string?> GetPasswordHashAsync(User user, CancellationToken ct) => Value(user.PasswordHash, ct);
    public Task<bool> HasPasswordAsync(User user, CancellationToken ct) => Value(user.PasswordHash != null, ct);
    public Task SetEmailAsync(User user, string? email, CancellationToken ct) => Change(() => user.Email = email!, ct);
    public Task<string?> GetEmailAsync(User user, CancellationToken ct) => Value<string?>(user.Email, ct);
    public Task<bool> GetEmailConfirmedAsync(User user, CancellationToken ct) => Value(user.EmailConfirmed, ct);
    public Task SetEmailConfirmedAsync(User user, bool confirmed, CancellationToken ct) => Change(() => user.EmailConfirmed = confirmed, ct);
    public Task<string?> GetNormalizedEmailAsync(User user, CancellationToken ct) => Value(user.NormalizedEmail, ct);
    public Task SetNormalizedEmailAsync(User user, string? email, CancellationToken ct) => Change(() => user.NormalizedEmail = email, ct);
    public Task SetSecurityStampAsync(User user, string stamp, CancellationToken ct) => Change(() => user.SecurityStamp = stamp, ct);
    public Task<string?> GetSecurityStampAsync(User user, CancellationToken ct) => Value<string?>(user.SecurityStamp, ct);
    public Task<DateTimeOffset?> GetLockoutEndDateAsync(User user, CancellationToken ct) => Value(user.LockoutEnd, ct);
    public Task SetLockoutEndDateAsync(User user, DateTimeOffset? end, CancellationToken ct) => Change(() => user.LockoutEnd = end, ct);
    public Task<int> IncrementAccessFailedCountAsync(User user, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(++user.AccessFailedCount); }
    public Task ResetAccessFailedCountAsync(User user, CancellationToken ct) => Change(() => user.AccessFailedCount = 0, ct);
    public Task<int> GetAccessFailedCountAsync(User user, CancellationToken ct) => Value(user.AccessFailedCount, ct);
    public Task<bool> GetLockoutEnabledAsync(User user, CancellationToken ct) => Value(user.LockoutEnabled, ct);
    public Task SetLockoutEnabledAsync(User user, bool enabled, CancellationToken ct) => Change(() => user.LockoutEnabled = enabled, ct);

    private static Task<T> Value<T>(T value, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(value); }
    private static Task Change(Action action, CancellationToken ct) { ct.ThrowIfCancellationRequested(); action(); return Task.CompletedTask; }
}
