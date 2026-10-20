using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OrionSearch.Infrastructure.Persistence;

public sealed class OrionSearchDbContextFactory : IDesignTimeDbContextFactory<OrionSearchDbContext>
{
    public OrionSearchDbContext CreateDbContext(string[] args)
    {
        var connectionString = PostgresConnection.Resolve(key => Environment.GetEnvironmentVariable(key.Replace(":", "__")));
        var options = new DbContextOptionsBuilder<OrionSearchDbContext>()
            .UseNpgsql(connectionString, postgres => postgres
                .SetPostgresVersion(16, 0)
                .MigrationsHistoryTable("__ef_migrations_history", "system"));
        return new OrionSearchDbContext(options.Options);
    }
}
