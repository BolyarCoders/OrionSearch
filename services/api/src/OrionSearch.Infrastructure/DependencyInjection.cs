using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrionSearch.Infrastructure.Persistence;

namespace OrionSearch.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<OrionSearchDbContext>(options => options.UseNpgsql(
            configuration.GetConnectionString("OrionSearch"),
            postgres => postgres.SetPostgresVersion(16, 0).MigrationsHistoryTable("__ef_migrations_history", "system")));
        services.AddHealthChecks().AddCheck<DatabaseReadinessCheck>("postgresql", timeout: TimeSpan.FromSeconds(5));
        return services;
    }
}
