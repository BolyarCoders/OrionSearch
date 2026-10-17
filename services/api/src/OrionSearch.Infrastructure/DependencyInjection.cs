using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrionSearch.Infrastructure.Persistence;
using OrionSearch.Application.Archives;
using OrionSearch.Infrastructure.Archives;

namespace OrionSearch.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<OrionSearchDbContext>(options => options.UseNpgsql(
            PostgresConnection.Resolve(key => configuration[key]),
            postgres => postgres.SetPostgresVersion(16, 0).MigrationsHistoryTable("__ef_migrations_history", "system")));
        services.AddHealthChecks().AddCheck<DatabaseReadinessCheck>("postgresql", timeout: TimeSpan.FromSeconds(5));
        services.AddScoped<IArchiveStore, EfArchiveStore>();
        services.AddScoped<ArchiveService>();
        return services;
    }
}
