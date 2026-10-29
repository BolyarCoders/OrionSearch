using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrionSearch.Infrastructure.Persistence;
using OrionSearch.Application.Archives;
using OrionSearch.Infrastructure.Archives;
using OrionSearch.Application.Research;
using OrionSearch.Infrastructure.Research;
using Microsoft.Extensions.Options;
using Amazon.Runtime;
using Amazon.S3;
using OrionSearch.Application.Content;
using OrionSearch.Infrastructure.Content;

namespace OrionSearch.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, bool includeUserServices = true)
    {
        services.AddDbContext<OrionSearchDbContext>(options => options.UseNpgsql(
            PostgresConnection.Resolve(key => configuration[key]),
            postgres => postgres.SetPostgresVersion(16, 0).MigrationsHistoryTable("__ef_migrations_history", "system")));
        services.AddHealthChecks().AddCheck<DatabaseReadinessCheck>("postgresql", timeout: TimeSpan.FromSeconds(5));
        if (includeUserServices)
        {
            services.AddScoped<IArchiveStore, EfArchiveStore>();
            services.AddScoped<ArchiveService>();
            services.AddScoped<ArchiveAuthorization>();
            services.AddScoped<ICaptureReader, EfCaptureReader>();
            services.AddScoped<CaptureService>();
            services.AddScoped<ResearchService>();
            services.AddScoped<IResearchResultsStore, EfResearchResultsStore>();
            services.AddScoped<ResearchResultsService>();
        }
        services.AddOptions<CaptureStoreOptions>().Bind(configuration.GetSection("CaptureStore"))
            .Validate(options => options.IsValid(), "CaptureStore requires a safe endpoint, private bucket and credentials.").ValidateOnStart();
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<CaptureStoreOptions>>().Value);
        services.AddSingleton<ICaptureStore>(provider =>
        {
            var options = provider.GetRequiredService<CaptureStoreOptions>();
            if (!options.Enabled) return new DisabledCaptureStore();
            return new S3CaptureStore(provider.GetRequiredService<IAmazonS3>(), options);
        });
        services.AddSingleton<IAmazonS3>(provider =>
        {
            var options = provider.GetRequiredService<CaptureStoreOptions>();
            return new AmazonS3Client(new BasicAWSCredentials(options.AccessKey, options.SecretKey), new AmazonS3Config
            {
                ServiceURL = options.Endpoint,
                AuthenticationRegion = options.Region,
                ForcePathStyle = true,
                MaxErrorRetry = 0,
                Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds)
            });
        });
        services.AddDbContextFactory<OrionSearchDbContext>(lifetime: ServiceLifetime.Scoped);
        services.AddOptions<ResearchWorkerOptions>().Bind(configuration.GetSection("ResearchWorker"))
            .Validate(options => options.IsValid(), "ResearchWorker settings contain invalid limits or an unsafe agent address.").ValidateOnStart();
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<ResearchWorkerOptions>>().Value);
        services.AddScoped<IResearchStore, EfResearchStore>();
        services.AddScoped<IResearchQueue, EfResearchQueue>();
        services.AddScoped<ResearchProcessor>();
        services.AddHttpClient<IResearchAgentClient, HttpResearchAgentClient>(client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, MaxResponseHeadersLength = 32 });
        services.AddHostedService<ResearchBackgroundService>();
        return services;
    }
}
