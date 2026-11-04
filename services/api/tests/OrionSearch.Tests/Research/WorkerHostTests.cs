using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrionSearch.Application.Research;
using OrionSearch.Tests.Archives;
using OrionSearch.Tests.Persistence;
using OrionSearch.Worker;

namespace OrionSearch.Tests.Research;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class WorkerHostTests(PostgreSqlFixture database)
{
    [Fact]
    public void DedicatedDependencyGraphDoesNotRequireAnHttpUser()
    {
        var builder = Builder(database.ConnectionString);
        using var services = builder.Services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = services.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<OrionSearch.Infrastructure.Research.ResearchProcessor>());
    }

    [Theory]
    [InlineData("ResearchWorker:Enabled", "false")]
    [InlineData("ResearchWorker:AgentApiKey", "")]
    [InlineData("ResearchWorker:AgentBaseAddress", "http://untrusted.example")]
    public async Task DedicatedHostRefusesDisabledOrUnconfiguredExecution(string key, string value)
    {
        var builder = Builder(database.ConnectionString);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { [key] = value });
        using var host = builder.Build();
        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }

    [Fact]
    public async Task StartupDoesNotMigrateAnUninitializedDatabase()
    {
        var connection = await database.CreateDatabaseAsync();
        try
        {
            using var host = Builder(connection).Build();
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());
            Assert.Contains("No migrations were applied", error.Message);
            await using var context = database.CreateContext(connection);
            Assert.Empty(await context.Database.GetAppliedMigrationsAsync());
            Assert.False(await context.Database.SqlQuery<bool>($"SELECT to_regclass('system.jobs') IS NOT NULL AS \"Value\"").SingleAsync());
        }
        finally { await database.DropDatabaseAsync(connection); }
    }

    [Fact]
    public async Task StartupRefusesAHistoryWithUnknownNewerMigrations()
    {
        var connection = await database.CreateDatabaseAsync();
        try
        {
            await using var context = database.CreateContext(connection);
            await context.Database.MigrateAsync();
            await context.Database.ExecuteSqlRawAsync("""
                INSERT INTO system.__ef_migrations_history ("MigrationId", "ProductVersion")
                VALUES ('99999999999999_UnknownFutureSchema', '10.0.13')
                """);
            using var host = Builder(connection).Build();
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());
            Assert.Contains("migration history is not current", error.Message);
            Assert.Contains("99999999999999_UnknownFutureSchema", await context.Database.GetAppliedMigrationsAsync());
        }
        finally { await database.DropDatabaseAsync(connection); }
    }

    [Fact]
    public async Task ANewHostRecoversAnInterruptedAttemptWithoutAnotherQueueOrPartialResults()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        var run = await ResearchTestData.SubmitAsync(scenario);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstAgent = new FakeResearchAgent(async (request, ct) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return ResearchTestData.Output(request.ResearchRunId);
        });
        var builder = Builder(scenario.ConnectionString);
        builder.Services.AddSingleton<IResearchAgentClient>(firstAgent);
        using (var first = builder.Build())
        {
            await first.StartAsync();
            try { await entered.Task.WaitAsync(TimeSpan.FromSeconds(15)); }
            finally { await first.StopAsync(); }
        }
        await using (var context = scenario.Context())
        {
            Assert.Empty(await context.Documents.ToListAsync());
            Assert.Equal("running", (await context.ResearchRuns.SingleAsync()).Status);
            Assert.Equal("processing", (await context.Jobs.SingleAsync()).Status);
        }
        await ResearchTestData.ExpireAsync(scenario);
        var secondAgent = new FakeResearchAgent((request, _) => Task.FromResult(ResearchTestData.Output(request.ResearchRunId)));
        builder = Builder(scenario.ConnectionString);
        builder.Services.AddSingleton<IResearchAgentClient>(secondAgent);
        using (var second = builder.Build())
        {
            await second.StartAsync();
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                await using var context = scenario.Context();
                while ((await context.ResearchRuns.AsNoTracking().SingleAsync(deadline.Token)).Status != "completed")
                    await Task.Delay(50, deadline.Token);
            }
            finally { await second.StopAsync(); }
        }
        Assert.Equal(1, firstAgent.Calls);
        Assert.Equal(1, secondAgent.Calls);
        await using var stored = scenario.Context();
        Assert.Single(await stored.Jobs.ToListAsync());
        Assert.Single(await stored.DocumentVersions.ToListAsync());
        Assert.Equal(new[] { "timed_out", "completed" }, await stored.JobAttempts.OrderBy(attempt => attempt.AttemptNumber).Select(attempt => attempt.Status).ToArrayAsync());
    }

    internal static HostApplicationBuilder Builder(string connection)
    {
        var builder = WorkerApplication.CreateBuilder([]);
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:OrionSearch"] = connection,
            ["ResearchWorker:Enabled"] = "true",
            ["ResearchWorker:AgentBaseAddress"] = "http://127.0.0.1:7999",
            ["ResearchWorker:AgentApiKey"] = "local-test-service-key",
            ["ResearchWorker:PollSeconds"] = "1",
            ["ResearchWorker:HeartbeatSeconds"] = "1",
            ["ResearchWorker:RetryBaseSeconds"] = "1"
        });
        builder.Logging.ClearProviders();
        return builder;
    }
}
