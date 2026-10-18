using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrionSearch.Infrastructure.Persistence;
using OrionSearch.Application.Archives;
using OrionSearch.Infrastructure.Archives;
using OrionSearch.Application.Research;
using OrionSearch.Infrastructure.Research;
using Microsoft.Extensions.Options;

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
        services.AddScoped<ArchiveAuthorization>();
        services.AddDbContextFactory<OrionSearchDbContext>(lifetime: ServiceLifetime.Scoped);
        services.AddOptions<ResearchWorkerOptions>().Bind(configuration.GetSection("ResearchWorker"))
            .Validate(options => options.IsValid(), "ResearchWorker settings contain invalid limits or an unsafe agent address.").ValidateOnStart();
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<ResearchWorkerOptions>>().Value);
        services.AddScoped<IResearchStore, EfResearchStore>();
        services.AddScoped<IResearchQueue, EfResearchQueue>();
        services.AddScoped<ResearchService>();
        services.AddScoped<ResearchProcessor>();
        services.AddHttpClient<IResearchAgentClient, HttpResearchAgentClient>(client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, MaxResponseHeadersLength = 32 });
        services.AddHostedService<ResearchBackgroundService>();
        return services;
    }
}
