using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrionSearch.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace OrionSearch.Tests.Persistence;

[CollectionDefinition("PostgreSQL")]
public sealed class PostgreSqlCollection : ICollectionFixture<PostgreSqlFixture>;

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly List<string> _databases = [];
    private PostgreSqlContainer? _container;
    private string _adminConnectionString = "";

    public string ConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        _adminConnectionString = Environment.GetEnvironmentVariable("ORIONSEARCH_TEST_POSTGRES") ?? "";
        if (string.IsNullOrWhiteSpace(_adminConnectionString))
        {
            _container = new PostgreSqlBuilder("postgres:18-alpine")
                .WithDatabase("postgres")
                .WithPassword(Guid.NewGuid().ToString("N"))
                .Build();
            await _container.StartAsync();
            _adminConnectionString = _container.GetConnectionString();
        }

        try
        {
            ConnectionString = await CreateDatabaseAsync();
            await using var context = CreateContext();
            await context.Database.MigrateAsync();
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public OrionSearchDbContext CreateContext(string? connectionString = null) =>
        new(new DbContextOptionsBuilder<OrionSearchDbContext>()
            .UseNpgsql(connectionString ?? ConnectionString, postgres => postgres
                .SetPostgresVersion(16, 0)
                .MigrationsHistoryTable("__ef_migrations_history", "system"))
            .Options);

    public async Task<string> CreateDatabaseAsync()
    {
        var name = $"orion_tests_{Guid.NewGuid():N}";
        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
        await command.ExecuteNonQueryAsync();
        _databases.Add(name);
        return new NpgsqlConnectionStringBuilder(_adminConnectionString) { Database = name, Pooling = false }.ConnectionString;
    }

    public async Task DisposeAsync()
    {
        if (_databases.Count > 0)
        {
            await using var connection = new NpgsqlConnection(_adminConnectionString);
            await connection.OpenAsync();
            foreach (var name in _databases)
            {
                await using var command = new NpgsqlCommand($"DROP DATABASE \"{name}\" WITH (FORCE)", connection);
                await command.ExecuteNonQueryAsync();
            }
            _databases.Clear();
        }
        if (_container is not null)
        {
            await _container.DisposeAsync();
            _container = null;
        }
    }
}
