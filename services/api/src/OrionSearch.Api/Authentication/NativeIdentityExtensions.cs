using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using OrionSearch.Domain.Auth;
using OrionSearch.Infrastructure.Authentication;
using OrionSearch.Infrastructure.Persistence;

namespace OrionSearch.Api.Authentication;

public static class NativeIdentityExtensions
{
    public static IServiceCollection AddNativeIdentity(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddOptions<NativeIdentitySettings>().Bind(configuration.GetSection("NativeIdentity"))
            .Validate(settings => settings.IsValid(environment.IsDevelopment()),
                "NativeIdentity requires valid request limits and, outside Development, a key-encryption certificate.")
            .ValidateOnStart();
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<NativeIdentitySettings>>().Value);
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("accounts", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = context.RequestServices.GetRequiredService<NativeIdentitySettings>().RequestsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));
        });
        services.AddIdentityCore<User>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 12;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            options.SignIn.RequireConfirmedEmail = false;
        }).AddUserStore<IdentityUserStore>().AddSignInManager()
            .AddClaimsPrincipalFactory<NativeUserPrincipalFactory>();
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, UnconfiguredAuthenticationHandler>("Unconfigured", _ => { })
            .AddScheme<AuthenticationSchemeOptions, NativeBearerHandler>(NativeBearerHandler.SchemeName, _ => { })
            .AddBearerToken(IdentityConstants.BearerScheme, options =>
        {
            options.BearerTokenExpiration = TimeSpan.FromMinutes(15);
            options.RefreshTokenExpiration = TimeSpan.FromDays(7);
        });
        services.AddOptions<AuthenticationOptions>().Configure<NativeIdentitySettings>((options, settings) =>
            options.DefaultScheme = settings.Enabled ? NativeBearerHandler.SchemeName : "Unconfigured");

        var protection = services.AddDataProtection().SetApplicationName("OrionSearch.Api")
            .PersistKeysToDbContext<OrionSearchDbContext>();
        var certificatePath = configuration["NativeIdentity:KeyEncryptionCertificatePath"];
        if (configuration.GetValue<bool>("NativeIdentity:Enabled") && !string.IsNullOrWhiteSpace(certificatePath))
        {
            var certificate = X509CertificateLoader.LoadPkcs12FromFile(certificatePath,
                configuration["NativeIdentity:KeyEncryptionCertificatePassword"], X509KeyStorageFlags.EphemeralKeySet);
            if (!certificate.HasPrivateKey) throw new InvalidOperationException("The data-protection certificate requires a private key.");
            // The framework also registers the certificate for decryption after a process restart.
            protection.ProtectKeysWithCertificate(certificate);
        }
        services.AddOptions<KeyManagementOptions>()
            .Validate(options => !configuration.GetValue<bool>("NativeIdentity:Enabled") || environment.IsDevelopment() || options.XmlEncryptor is not null,
                "Production Identity requires encrypted persistent token keys.").ValidateOnStart();
        return services;
    }
}
