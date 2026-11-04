using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using OrionSearch.Domain.Auth;
using OrionSearch.Infrastructure.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;
using OrionSearch.Api.Authentication;
using OrionSearch.Infrastructure;

namespace OrionSearch.Tests.Persistence;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class NativeIdentityMigrationTests(PostgreSqlFixture database)
{
    private const string PreviousMigration = "20261018073825_ImplementResearchExecution";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProductionCertificateConfigurationDecryptsPersistedKeysInANewProvider(bool useEncodedCertificate)
    {
        var connectionString = await database.CreateDatabaseAsync();
        await using (var context = database.CreateContext(connectionString)) await context.Database.MigrateAsync();
        var path = Path.Combine(Path.GetTempPath(), $"orion-identity-{Guid.NewGuid():N}.pfx");
        var password = Guid.NewGuid().ToString("N");
        try
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=OrionSearch Test Keys", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
            var pfx = certificate.Export(X509ContentType.Pfx, password);
            await File.WriteAllBytesAsync(path, pfx);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:OrionSearch"] = connectionString,
                ["NativeIdentity:Enabled"] = "true",
                ["NativeIdentity:KeyEncryptionCertificatePath"] = useEncodedCertificate ? null : path,
                ["NativeIdentity:KeyEncryptionCertificateBase64"] = useEncodedCertificate ? Convert.ToBase64String(pfx) : null,
                ["NativeIdentity:KeyEncryptionCertificatePassword"] = password
            }).Build();
            ServiceProvider Provider()
            {
                var services = new ServiceCollection();
                services.AddLogging();
                services.AddInfrastructure(configuration);
                services.AddNativeIdentity(configuration, new HostingEnvironment { EnvironmentName = "Production" });
                return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            }
            string encrypted;
            using (var first = Provider())
                encrypted = first.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Protect("retained ticket");
            using (var second = Provider())
                Assert.Equal("retained ticket", second.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Unprotect(encrypted));
            await using var verify = database.CreateContext(connectionString);
            Assert.All(await verify.DataProtectionKeys.ToListAsync(), key => Assert.Contains("encryptedSecret", key.Xml));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task UpgradePreservesExistingUserArchiveAndMembershipWithoutGrantingVerifiedIdentity()
    {
        var connectionString = await database.CreateDatabaseAsync();
        await using var context = database.CreateContext(connectionString);
        await context.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        var userId = Guid.NewGuid();
        var archiveId = Guid.NewGuid();
        await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO auth.users (id, email, display_name) VALUES ({userId}, {"legacy@example.com"}, {"Legacy user"})");
        await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO research.archives (id, owner_id, title) VALUES ({archiveId}, {userId}, {"Existing archive"})");
        await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO auth.archive_members (archive_id, user_id, role) VALUES ({archiveId}, {userId}, {"owner"})");
        await context.Database.MigrateAsync();
        await context.Database.MigrateAsync();
        var user = await context.Users.SingleAsync();
        Assert.Equal(userId, user.Id);
        Assert.Null(user.PasswordHash);
        Assert.Null(user.NormalizedEmail);
        Assert.False(user.EmailConfirmed);
        Assert.Equal("Legacy user", user.DisplayName);
        Assert.Equal(userId, (await context.Archives.SingleAsync()).OwnerId);
        Assert.Equal(userId, (await context.ArchiveMembers.SingleAsync()).UserId);
        Assert.Equal(context.Database.GetMigrations().Count(), (await context.Database.GetAppliedMigrationsAsync()).Count());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DowngradeRefusesToDiscardEnrolledAccountsOrPersistedKeys(bool enrolledUser)
    {
        var connectionString = await database.CreateDatabaseAsync();
        await using var context = database.CreateContext(connectionString);
        await context.Database.MigrateAsync();
        if (enrolledUser)
            context.Users.Add(new User { Email = "native@example.com", NormalizedEmail = "NATIVE@EXAMPLE.COM", DisplayName = "Native account" });
        else
            context.DataProtectionKeys.Add(new DataProtectionKey { FriendlyName = "test", Xml = "<key />" });
        await context.SaveChangesAsync();
        var exception = await Assert.ThrowsAsync<PostgresException>(() => context.GetService<IMigrator>().MigrateAsync(PreviousMigration));
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal(9, (await context.Database.GetAppliedMigrationsAsync()).Count());
        if (enrolledUser) Assert.Single(await context.Users.ToListAsync());
        else Assert.Single(await context.DataProtectionKeys.ToListAsync());
    }

    [Fact]
    public async Task StaleIdentityUpdateCannotOverwriteANewerAccountChange()
    {
        var connectionString = await database.CreateDatabaseAsync();
        await using var first = database.CreateContext(connectionString);
        await first.Database.MigrateAsync();
        var user = new User { Email = "native@example.com", DisplayName = "Original" };
        Assert.True((await new IdentityUserStore(first).CreateAsync(user, default)).Succeeded);
        await using var second = database.CreateContext(connectionString);
        var stale = await second.Users.SingleAsync();
        user.Status = "disabled";
        Assert.True((await new IdentityUserStore(first).UpdateAsync(user, default)).Succeeded);
        stale.DisplayName = "Stale edit";
        var failed = await new IdentityUserStore(second).UpdateAsync(stale, default);
        Assert.False(failed.Succeeded);
        Assert.Contains(failed.Errors, error => error.Code == "ConcurrencyFailure");
        second.ChangeTracker.Clear();
        Assert.Equal("disabled", (await second.Users.SingleAsync()).Status);
    }
}
