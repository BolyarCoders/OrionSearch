using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrionSearch.Domain.Auth;
using OrionSearch.Domain.Research;

namespace OrionSearch.Tests.Persistence;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class ArchivePersistenceTests(PostgreSqlFixture database)
{
    [Fact]
    public async Task ArchiveSettingsAndOwnerMembershipCanBeCreatedInOneTransaction()
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var archive = await TestData.CreateArchiveAsync(context);
        context.ArchiveMembers.Add(new ArchiveMember { ArchiveId = archive.Id, UserId = archive.OwnerId, Role = "owner" });
        context.ArchiveSettings.Add(new ArchiveSettings
        {
            ArchiveId = archive.Id,
            DateFrom = new DateOnly(2026, 1, 1),
            DateTo = new DateOnly(2026, 10, 16),
            Languages = ["en", "bg"],
            Config = "{\"depth\":2}"
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var settings = await context.ArchiveSettings.SingleAsync(x => x.ArchiveId == archive.Id);
        var owner = await context.ArchiveMembers.SingleAsync(x => x.ArchiveId == archive.Id);
        var user = await context.Users.SingleAsync(x => x.Id == archive.OwnerId);
        using var config = JsonDocument.Parse(settings.Config);
        Assert.Equal(archive.OwnerId, owner.UserId);
        Assert.Equal("owner", owner.Role);
        Assert.Equal(new DateOnly(2026, 1, 1), settings.DateFrom);
        Assert.Equal(new[] { "en", "bg" }, settings.Languages);
        Assert.Empty(settings.PreferredDomains);
        Assert.Empty(settings.ExcludedDomains);
        Assert.Equal(2, config.RootElement.GetProperty("depth").GetInt32());
        Assert.NotEqual(Guid.Empty, user.Id);
        Assert.Equal(DateTimeKind.Utc, user.CreatedAt.Kind);
        Assert.Equal(DateTimeKind.Utc, user.UpdatedAt.Kind);
        Assert.Null(user.PasswordHash);
    }

    [Fact]
    public async Task UserEmailIsUniqueRegardlessOfCase()
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var user = await TestData.CreateUserAsync(context);
        context.Users.Add(new User { Email = user.Email.ToUpperInvariant(), DisplayName = "Duplicate" });

        await TestData.AssertRejectedAsync(() => context.SaveChangesAsync(), PostgresErrorCodes.UniqueViolation, "ux_users_email");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InvalidUserAndArchiveStatusesAreRejected(bool invalidUser)
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var archive = await TestData.CreateArchiveAsync(context);
        if (invalidUser)
        {
            var user = await context.Users.SingleAsync(x => x.Id == archive.OwnerId);
            user.Status = "invalid";
        }
        else
        {
            archive.Status = "invalid";
        }

        await TestData.AssertRejectedAsync(() => context.SaveChangesAsync(), PostgresErrorCodes.CheckViolation,
            invalidUser ? "ck_users_status" : "ck_archives_status");
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("editor")]
    [InlineData("viewer")]
    public async Task ValidMembershipRolesAreStored(string role)
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var archive = await TestData.CreateArchiveAsync(context);
        context.ArchiveMembers.Add(new ArchiveMember { ArchiveId = archive.Id, UserId = archive.OwnerId, Role = role });

        await context.SaveChangesAsync();

        Assert.Equal(role, (await context.ArchiveMembers.SingleAsync(x => x.ArchiveId == archive.Id)).Role);
    }

    [Fact]
    public async Task InvalidMembershipRolesAreRejected()
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var archive = await TestData.CreateArchiveAsync(context);
        context.ArchiveMembers.Add(new ArchiveMember { ArchiveId = archive.Id, UserId = archive.OwnerId, Role = "admin" });

        await TestData.AssertRejectedAsync(() => context.SaveChangesAsync(), PostgresErrorCodes.CheckViolation, "ck_archive_members_role");
    }

    [Fact]
    public async Task DuplicateMembershipIsRejectedByPostgreSql()
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var archive = await TestData.CreateArchiveAsync(context);
        context.ArchiveMembers.Add(new ArchiveMember { ArchiveId = archive.Id, UserId = archive.OwnerId });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        context.ArchiveMembers.Add(new ArchiveMember { ArchiveId = archive.Id, UserId = archive.OwnerId });

        await TestData.AssertRejectedAsync(() => context.SaveChangesAsync(), PostgresErrorCodes.UniqueViolation, "pk_archive_members");
    }

    [Fact]
    public async Task ArchiveCannotHaveTwoOwnerMemberships()
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var archive = await TestData.CreateArchiveAsync(context);
        var otherUser = await TestData.CreateUserAsync(context);
        context.ArchiveMembers.Add(new ArchiveMember { ArchiveId = archive.Id, UserId = archive.OwnerId, Role = "owner" });
        await context.SaveChangesAsync();
        context.ArchiveMembers.Add(new ArchiveMember { ArchiveId = archive.Id, UserId = otherUser.Id, Role = "owner" });

        await TestData.AssertRejectedAsync(() => context.SaveChangesAsync(), PostgresErrorCodes.UniqueViolation, "ux_archive_members_owner");
    }

    [Fact]
    public async Task ReversedDateRangeIsRejected()
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var archive = await TestData.CreateArchiveAsync(context);
        context.ArchiveSettings.Add(new ArchiveSettings
        {
            ArchiveId = archive.Id,
            DateFrom = new DateOnly(2026, 10, 16),
            DateTo = new DateOnly(2026, 1, 1)
        });

        await TestData.AssertRejectedAsync(() => context.SaveChangesAsync(), PostgresErrorCodes.CheckViolation, "ck_archive_settings_dates");
    }

    [Fact]
    public async Task RawInsertsReceiveJsonArrayAndUtcDefaults()
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var archive = await TestData.CreateArchiveAsync(context);
        await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO research.archive_settings (archive_id) VALUES ({archive.Id})");

        var settings = await context.ArchiveSettings.SingleAsync(x => x.ArchiveId == archive.Id);

        Assert.Equal("{}", settings.Config);
        Assert.Empty(settings.Languages);
        Assert.Empty(settings.PreferredDomains);
        Assert.Empty(settings.ExcludedDomains);
        Assert.Null(settings.DateFrom);
        Assert.Null(settings.DateTo);
        Assert.Equal(DateTimeKind.Utc, settings.UpdatedAt.Kind);
    }

    [Fact]
    public async Task UpdatedAtIsMaintainedForSqlAndEfUpdates()
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var user = await TestData.CreateUserAsync(context);
        var previousTimestamp = user.UpdatedAt;
        user.DisplayName = "Updated name";
        await context.SaveChangesAsync();
        Assert.True(user.UpdatedAt >= previousTimestamp);
        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE auth.users SET display_name = 'SQL update', updated_at = '2000-01-01T00:00:00Z' WHERE id = {user.Id}");
        await context.Entry(user).ReloadAsync();

        Assert.True(user.UpdatedAt >= previousTimestamp);
        Assert.Equal("SQL update", user.DisplayName);
    }

    [Fact]
    public async Task DeletingAnEmptyArchiveRemovesItsSettingsAndMemberships()
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var archive = await TestData.CreateArchiveAsync(context);
        context.ArchiveSettings.Add(new ArchiveSettings { ArchiveId = archive.Id });
        context.ArchiveMembers.Add(new ArchiveMember { ArchiveId = archive.Id, UserId = archive.OwnerId, Role = "owner" });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        await context.Archives.Where(x => x.Id == archive.Id).ExecuteDeleteAsync();

        Assert.False(await context.ArchiveSettings.AnyAsync(x => x.ArchiveId == archive.Id));
        Assert.False(await context.ArchiveMembers.AnyAsync(x => x.ArchiveId == archive.Id));
        Assert.True(await context.Users.AnyAsync(x => x.Id == archive.OwnerId));
    }

    [Fact]
    public async Task ArchiveOwnerCannotBeDeleted()
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var archive = await TestData.CreateArchiveAsync(context);
        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Users.Where(x => x.Id == archive.OwnerId).ExecuteDeleteAsync());

        TestData.AssertRestricted(exception, "FK_archives_users_owner_id");
    }
}
