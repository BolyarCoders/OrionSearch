using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using OrionSearch.Api.Configuration;
using OrionSearch.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using OrionSearch.Api.Authentication;
using OrionSearch.Api.Archives;
using OrionSearch.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;

namespace OrionSearch.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddInfrastructure(configuration);
        services.AddOptions<CorsSettings>()
            .Bind(configuration.GetSection(CorsSettings.SectionName))
            .Validate(settings => settings.AllowedOrigins.All(CorsSettings.IsValidOrigin),
                "Cors:AllowedOrigins must contain HTTP(S) origins without paths or trailing slashes.")
            .ValidateOnStart();

        services.AddOptions<ReverseProxySettings>()
            .Bind(configuration.GetSection(ReverseProxySettings.SectionName))
            .Validate(settings => settings.KnownProxies.All(proxy => IPAddress.TryParse(proxy, out _)),
                "ReverseProxy:KnownProxies must contain IP addresses.")
            .ValidateOnStart();

        services.AddCors();
        services.AddOptions<Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions>()
            .Configure<IOptions<CorsSettings>>((options, settings) =>
            {
                options.AddDefaultPolicy(policy => policy
                    .WithOrigins(settings.Value.AllowedOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod());
            });

        services.AddOptions<ForwardedHeadersOptions>()
            .Configure<IOptions<ReverseProxySettings>>((options, settings) =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.ForwardLimit = 1;
                foreach (var proxy in settings.Value.KnownProxies)
                {
                    options.KnownProxies.Add(IPAddress.Parse(proxy));
                }
            });

        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                context.ProblemDetails.Instance = context.HttpContext.Request.Path;
                context.ProblemDetails.Extensions["traceId"] =
                    Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;

                if (context.ProblemDetails.Status == StatusCodes.Status500InternalServerError)
                {
                    context.ProblemDetails.Title = "An unexpected error occurred.";
                    context.ProblemDetails.Detail = null;
                }
            };
        });

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, TrustedCurrentUser>();
        services.AddAuthentication("Unconfigured")
            .AddScheme<AuthenticationSchemeOptions, UnconfiguredAuthenticationHandler>("Unconfigured", _ => { });
        services.AddAuthorization();
        services.AddExceptionHandler<ArchiveExceptionHandler>();
        services.AddOpenApi(options => options.AddOperationTransformer((operation, context, _) =>
        {
            if (context.Description.ActionDescriptor.EndpointMetadata.OfType<IAuthorizeData>().Any())
            {
                operation.Description += " Authentication is currently unconfigured: normal startup returns 401 until a validated provider maps its subject to the internal application user.";
            }
            return Task.CompletedTask;
        }));
        return services;
    }
}
