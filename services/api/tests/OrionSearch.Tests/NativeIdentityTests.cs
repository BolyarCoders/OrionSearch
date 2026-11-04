using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OrionSearch.Domain.Auth;
using OrionSearch.Tests.Persistence;

namespace OrionSearch.Tests;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class NativeIdentityTests(PostgreSqlFixture database)
{
    private const string Password = "Research!Account2026";
    private const string NewPassword = "Changed!Account2026";

    [Fact]
    public async Task EmailAndPasswordRegistrationCanImmediatelyLoginCreateArchiveAndReadProfile()
    {
        await using var scenario = await Scenario.CreateAsync(database);
        using var client = scenario.Client();
        await scenario.RegisterAsync(client);
        var tokens = await scenario.LoginAsync(client);
        Assert.Equal("Bearer", tokens.GetProperty("tokenType").GetString());
        Assert.Equal(900, tokens.GetProperty("expiresIn").GetInt32());
        var access = tokens.GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", access);
        using var me = await client.GetAsync("/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.True(me.Headers.CacheControl?.NoStore);
        Assert.Equal("no-referrer", Assert.Single(me.Headers.GetValues("Referrer-Policy")));
        var profile = await me.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("researcher", profile.GetProperty("displayName").GetString());
        Assert.False(profile.TryGetProperty("emailConfirmed", out _));
        using var archive = await client.PostAsJsonAsync("/archives", new { title = "Native Identity archive", settings = new { } });
        Assert.Equal(HttpStatusCode.Created, archive.StatusCode);
        var id = (await archive.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("archive").GetProperty("id").GetGuid();

        using var outsider = scenario.Client();
        await scenario.RegisterAsync(outsider, "outsider@example.com");
        var otherTokens = await scenario.LoginAsync(outsider, "outsider@example.com");
        outsider.DefaultRequestHeaders.Authorization = new("Bearer", otherTokens.GetProperty("accessToken").GetString());
        using var denied = await outsider.GetAsync($"/archives/{id}");
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var stored = (await users.FindByEmailAsync("researcher@example.com"))!;
        Assert.NotEqual(Password, stored.PasswordHash);
        Assert.True(await users.CheckPasswordAsync(stored, Password));
        Assert.False(stored.EmailConfirmed);
        Assert.Equal(profile.GetProperty("id").GetGuid(), stored.Id);
    }

    [Fact]
    public async Task FiveFailedPasswordAttemptsLockTheAccount()
    {
        await using var scenario = await Scenario.CreateAsync(database);
        using var client = scenario.Client();
        await scenario.RegisterAsync(client);
        await scenario.LoginAsync(client);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var bad = await client.PostAsJsonAsync("/auth/login", new { email = "researcher@example.com", password = "Wrong!Password2026" });
            Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
        }
        using var locked = await client.PostAsJsonAsync("/auth/login", new { email = "researcher@example.com", password = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        Assert.True(await users.IsLockedOutAsync((await users.FindByEmailAsync("researcher@example.com"))!));
    }

    [Theory]
    [InlineData("logout")]
    [InlineData("disabled")]
    [InlineData("change-password")]
    public async Task AccountChangesRevokeBothAccessAndRefreshTokens(string action)
    {
        await using var scenario = await Scenario.CreateAsync(database);
        using var client = scenario.Client();
        await scenario.RegisterAsync(client);
        var tokens = await scenario.LoginAsync(client);
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokens.GetProperty("accessToken").GetString());
        if (action == "disabled")
        {
            await using var scope = scenario.Factory.Services.CreateAsyncScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var user = (await users.FindByEmailAsync("researcher@example.com"))!;
            user.Status = "disabled";
            Assert.True((await users.UpdateAsync(user)).Succeeded);
        }
        else
        {
            using var result = action == "logout" ? await client.PostAsync("/auth/logout", null)
                : await client.PostAsJsonAsync("/auth/change-password", new { currentPassword = Password, newPassword = NewPassword });
            Assert.Equal(HttpStatusCode.NoContent, result.StatusCode);
        }
        using var me = await client.GetAsync("/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        using var refresh = await client.PostAsJsonAsync("/auth/refresh", new { refreshToken = tokens.GetProperty("refreshToken").GetString() });
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task RefreshSurvivesHostRestartButTamperedExpiredAndAccessTokensAreRejected()
    {
        await using var scenario = await Scenario.CreateAsync(database, encryptKeys: true);
        using var client = scenario.Client();
        await scenario.RegisterAsync(client);
        var tokens = await scenario.LoginAsync(client);
        await using (var context = database.CreateContext(scenario.ConnectionString))
        {
            var keys = await context.DataProtectionKeys.ToListAsync();
            Assert.NotEmpty(keys);
            Assert.All(keys, key => Assert.Contains("encryptedSecret", key.Xml));
        }
        scenario.Restart();
        using var restarted = scenario.Client();
        using var refresh = await restarted.PostAsJsonAsync("/auth/refresh", new { refreshToken = tokens.GetProperty("refreshToken").GetString() });
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        foreach (var token in new[] { "invalid-token", tokens.GetProperty("accessToken").GetString()! })
        {
            using var rejected = await restarted.PostAsJsonAsync("/auth/refresh", new { refreshToken = token });
            Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        }
        var options = scenario.Factory.Services.GetRequiredService<IOptionsMonitor<BearerTokenOptions>>().Get(IdentityConstants.BearerScheme);
        var ticket = options.RefreshTokenProtector.Unprotect(tokens.GetProperty("refreshToken").GetString()!)!;
        ticket.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
        using var expired = await restarted.PostAsJsonAsync("/auth/refresh", new { refreshToken = options.RefreshTokenProtector.Protect(ticket) });
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        ticket.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
        restarted.DefaultRequestHeaders.Authorization = new("Bearer", options.BearerTokenProtector.Protect(ticket));
        using var expiredAccess = await restarted.GetAsync("/me");
        Assert.Equal(HttpStatusCode.Unauthorized, expiredAccess.StatusCode);
    }

    [Fact]
    public async Task ConcurrentCaseInsensitiveRegistrationCreatesOneAccountAndNeverLinksAnExistingUser()
    {
        await using var scenario = await Scenario.CreateAsync(database);
        using var first = scenario.Client();
        using var second = scenario.Client();
        var results = await Task.WhenAll(
            first.PostAsJsonAsync("/auth/register", new { email = "researcher@example.com", password = Password }),
            second.PostAsJsonAsync("/auth/register", new { email = "RESEARCHER@example.com", password = Password }));
        try
        {
            Assert.Single(results, result => result.StatusCode == HttpStatusCode.Created);
            Assert.Single(results, result => result.StatusCode == HttpStatusCode.BadRequest);
            await using var context = database.CreateContext(scenario.ConnectionString);
            Assert.Single(await context.Users.ToListAsync());
            Assert.Equal(1, await context.Users.CountAsync(user => user.NormalizedEmail == "RESEARCHER@EXAMPLE.COM"));
        }
        finally { foreach (var result in results) result.Dispose(); }
    }

    [Fact]
    public async Task UnknownPropertiesCannotSelectUserIdOrRolesAndWeakPasswordsAreRejected()
    {
        await using var scenario = await Scenario.CreateAsync(database);
        using var client = scenario.Client();
        using var forged = await client.PostAsJsonAsync("/auth/register", new
        {
            email = "researcher@example.com",
            password = Password,
            id = Guid.NewGuid(),
            role = "owner"
        });
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        using var weak = await client.PostAsJsonAsync("/auth/register", new { email = "researcher@example.com", password = "weak" });
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        await using var context = database.CreateContext(scenario.ConnectionString);
        Assert.Empty(await context.Users.ToListAsync());
        client.DefaultRequestHeaders.Authorization = new("Bearer", "invalid");
        client.DefaultRequestHeaders.Add("X-User-Id", Guid.NewGuid().ToString());
        using var me = await client.GetAsync("/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task AuthRateAndBodyLimitsAreEnforced()
    {
        await using var scenario = await Scenario.CreateAsync(database, requestsPerMinute: 1);
        using var client = scenario.Client();
        using var oversized = await client.PostAsJsonAsync("/auth/register", new { email = new string('a', 40000) });
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
        using var first = await client.PostAsJsonAsync("/auth/login", new { email = "unknown@example.com", password = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        using var limited = await client.PostAsJsonAsync("/auth/login", new { email = "unknown@example.com", password = Password });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("unrecognized-legacy-password-hash")]
    public async Task ExistingLegacyUserCannotBeTakenOverThroughRegistration(string? legacyHash)
    {
        await using var scenario = await Scenario.CreateAsync(database);
        Guid id;
        await using (var context = database.CreateContext(scenario.ConnectionString))
        {
            var user = new User { Email = "legacy@example.com", DisplayName = "Existing owner", PasswordHash = legacyHash };
            context.Users.Add(user);
            await context.SaveChangesAsync();
            id = user.Id;
        }
        using var client = scenario.Client();
        using var registration = await client.PostAsJsonAsync("/auth/register", new { email = "LEGACY@example.com", password = Password });
        Assert.Equal(HttpStatusCode.BadRequest, registration.StatusCode);
        await using var verify = database.CreateContext(scenario.ConnectionString);
        var stored = await verify.Users.SingleAsync();
        Assert.Equal(id, stored.Id);
        Assert.Equal(legacyHash, stored.PasswordHash);
        Assert.Equal("Existing owner", stored.DisplayName);
        using var login = await client.PostAsJsonAsync("/auth/login", new { email = "legacy@example.com", password = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Theory]
    [InlineData("GET", "/auth/confirm-email")]
    [InlineData("POST", "/auth/resend-confirmation")]
    [InlineData("POST", "/auth/forgot-password")]
    [InlineData("POST", "/auth/reset-password")]
    public async Task EmailAndUnauthenticatedRecoveryEndpointsAreNotExposed(string method, string path)
    {
        await using var scenario = await Scenario.CreateAsync(database);
        using var client = scenario.Client();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed class Scenario(PostgreSqlFixture database, string connectionString, int requestsPerMinute) : IAsyncDisposable
    {
        private readonly ApiFactory _root = new("Development");
        public WebApplicationFactory<Program> Factory { get; private set; } = null!;
        public string ConnectionString => connectionString;
        private X509Certificate2? _certificate;

        public static async Task<Scenario> CreateAsync(PostgreSqlFixture database, int requestsPerMinute = 1000, bool encryptKeys = false)
        {
            var scenario = new Scenario(database, await database.CreateDatabaseAsync(), requestsPerMinute);
            await using var context = database.CreateContext(scenario.ConnectionString);
            await context.Database.MigrateAsync();
            if (encryptKeys)
            {
                using var rsa = RSA.Create(2048);
                var request = new CertificateRequest("CN=OrionSearch Tests", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
                scenario._certificate = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), null,
                    X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);
            }
            scenario.Start();
            return scenario;
        }

        private void Start() => Factory = _root.WithSettings(new()
        {
            ["ConnectionStrings:OrionSearch"] = ConnectionString,
            ["NativeIdentity:Enabled"] = "true",
            ["NativeIdentity:RequestsPerMinute"] = requestsPerMinute.ToString()
        }).WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            if (_certificate is not null) services.AddDataProtection().ProtectKeysWithCertificate(_certificate);
        }));

        public HttpClient Client() => Factory.CreateClient();
        public void Restart() { Factory.Dispose(); Start(); }
        public async Task RegisterAsync(HttpClient client, string email = "researcher@example.com")
        {
            using var registration = await client.PostAsJsonAsync("/auth/register", new { email, password = Password });
            Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        }
        public async Task<JsonElement> LoginAsync(HttpClient client, string email = "researcher@example.com", string password = Password)
        {
            using var response = await client.PostAsJsonAsync("/auth/login", new { email, password });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
        public async ValueTask DisposeAsync()
        {
            Factory.Dispose();
            _root.Dispose();
            _certificate?.Dispose();
            await database.DropDatabaseAsync(ConnectionString);
        }
    }

}
