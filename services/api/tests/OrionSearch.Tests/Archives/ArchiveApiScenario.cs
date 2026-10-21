using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrionSearch.Api.Authentication;
using OrionSearch.Domain.Auth;
using OrionSearch.Domain.Research;
using OrionSearch.Infrastructure.Persistence;
using OrionSearch.Tests.Persistence;

namespace OrionSearch.Tests.Archives;

internal sealed class ArchiveApiScenario(PostgreSqlFixture database, string connectionString) : IAsyncDisposable
{
    private readonly ApiFactory _root = new();
    private WebApplicationFactory<Program>? _factory;
    public Dictionary<string, Guid> Users { get; } = [];
    public Guid ArchiveId { get; private set; }
    public string ConnectionString { get; } = connectionString;
    public OrionSearchDbContext Context() => database.CreateContext(ConnectionString);
    public AsyncServiceScope Scope() => _factory!.Services.CreateAsyncScope();

    public static async Task<ArchiveApiScenario> CreateAsync(PostgreSqlFixture database)
    {
        var scenario = new ArchiveApiScenario(database, await database.CreateDatabaseAsync());
        await using var context = scenario.Context();
        await context.Database.MigrateAsync();
        foreach (var role in new[] { "owner", "editor", "viewer", "nonmember", "disabled" })
        {
            var user = await TestData.CreateUserAsync(context);
            if (role == "disabled") user.Status = "disabled";
            scenario.Users[role] = user.Id;
        }
        var archive = new Archive { OwnerId = scenario.Users["owner"], Title = "Existing archive", Description = "Keep this description" };
        context.Archives.Add(archive);
        await context.SaveChangesAsync();
        scenario.ArchiveId = archive.Id;
        context.ArchiveSettings.Add(new ArchiveSettings
        {
            ArchiveId = archive.Id,
            DateFrom = new DateOnly(2026, 5, 1),
            DateTo = new DateOnly(2026, 5, 20),
            Languages = ["en"],
            PreferredDomains = ["example.com"],
            Config = "{\"keep\":true}"
        });
        foreach (var role in new[] { "owner", "editor", "viewer" })
            context.ArchiveMembers.Add(new ArchiveMember { ArchiveId = archive.Id, UserId = scenario.Users[role], Role = role });
        await context.SaveChangesAsync();
        scenario._factory = scenario._root.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:OrionSearch"] = scenario.ConnectionString }));
            builder.ConfigureServices(services => services.AddAuthentication("ArchiveTest")
                .AddScheme<AuthenticationSchemeOptions, ArchiveTestAuthenticationHandler>("ArchiveTest", _ => { }));
        });
        return scenario;
    }

    public HttpClient Client(string? role = "owner")
    {
        var client = _factory!.CreateClient();
        if (role is not null) client.DefaultRequestHeaders.Add("X-Test-Identity", Users.TryGetValue(role, out var id) ? id.ToString() : role);
        return client;
    }

    public Task<HttpResponseMessage> PatchAsync(HttpClient client, object body) => client.PatchAsJsonAsync($"/archives/{ArchiveId}", body);

    public async ValueTask DisposeAsync()
    {
        _factory?.Dispose();
        _root.Dispose();
        await database.DropDatabaseAsync(ConnectionString);
    }
}

// This header is consumed only in this test assembly. Normal startup never registers this scheme.
internal sealed class ArchiveTestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-Identity", out var value)) return Task.FromResult(AuthenticateResult.NoResult());
        var claims = new List<Claim> { new(ClaimTypes.Role, "owner") };
        if (value != "missing") claims.Add(new(TrustedCurrentUser.InternalUserIdClaim, value.ToString()));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
}
