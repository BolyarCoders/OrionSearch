using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrionSearch.Application.Research;
using OrionSearch.Infrastructure;

namespace OrionSearch.Worker;

public static class WorkerApplication
{
    public static async Task Main(string[] args)
    {
        using var host = CreateBuilder(args).Build();
        await host.RunAsync();
    }

    public static HostApplicationBuilder CreateBuilder(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.AddJsonConsole();
        // Startup checks finish before the shared processing service can claim work.
        builder.Services.AddHostedService<WorkerDatabaseCheck>();
        builder.Services.AddInfrastructure(builder.Configuration, includeUserServices: false);
        builder.Services.AddOptions<ResearchWorkerOptions>().Validate(options => options.Enabled && options.HasAgentConfiguration(),
            "Dedicated research execution requires ResearchWorker:Enabled=true and a trusted agent address/service key.").ValidateOnStart();
        builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(30));
        return builder;
    }
}
