using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace OrionSearch.Tests;

internal sealed class ApiFactory(string environment = "Production") : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment(environment);

    public WebApplicationFactory<Program> WithSettings(Dictionary<string, string?> settings) =>
        WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(settings)));

    public WebApplicationFactory<Program> WithProbe(
        IPAddress? remoteAddress = null, Dictionary<string, string?>? settings = null) =>
        WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
                services.AddSingleton<IStartupFilter>(new RequestProbe(remoteAddress)));
            if (settings is not null)
            {
                builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
            }
        });

    private sealed class RequestProbe(IPAddress? remoteAddress) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            if (remoteAddress is not null)
            {
                app.Use((context, nextMiddleware) =>
                {
                    context.Connection.RemoteIpAddress = remoteAddress;
                    return nextMiddleware(context);
                });
            }

            next(app);

            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Path == "/__test/throw")
                {
                    throw new InvalidOperationException("Sensitive exception text for the test.");
                }

                if (context.Request.Path == "/__test/request")
                {
                    await context.Response.WriteAsJsonAsync(new
                    {
                        scheme = context.Request.Scheme,
                        remoteIp = context.Connection.RemoteIpAddress?.ToString()
                    });
                    return;
                }

                await nextMiddleware(context);
            });
        };
    }
}
