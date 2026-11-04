using OrionSearch.Api.Authentication;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;

namespace OrionSearch.Tests;

public sealed class NativeIdentityConfigurationTests
{
    [Fact]
    public void DisabledIdentityDoesNotLoadAnUnusedCertificate()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["NativeIdentity:Enabled"] = "false",
            ["NativeIdentity:KeyEncryptionCertificatePath"] = Path.Combine(Path.GetTempPath(), "missing-identity-certificate.pfx")
        }).Build();
        var services = new ServiceCollection();
        services.AddNativeIdentity(configuration, new HostingEnvironment { EnvironmentName = "Production" });
    }
    [Fact]
    public async Task DisabledIdentityDoesNotExposeAccountEndpoints()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var registration = await client.PostAsJsonAsync("/auth/register", new { email = "test@example.com", password = "Password!Test123" });
        using var me = await client.GetAsync("/me");
        Assert.Equal(HttpStatusCode.NotFound, registration.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, me.StatusCode);
    }

    [Fact]
    public void EnablingProductionIdentityWithoutRequiredSettingsFailsStartup()
    {
        using var factory = new ApiFactory();
        using var enabled = factory.WithSettings(new() { ["NativeIdentity:Enabled"] = "true" });
        Assert.Throws<OptionsValidationException>(() => enabled.CreateClient());
    }

    [Fact]
    public void ProductionRequiresAKeyEncryptionCertificateButNoEmailConfiguration()
    {
        var settings = new NativeIdentitySettings { Enabled = true };
        Assert.False(settings.IsValid(development: false));
        Assert.True(settings.IsValid(development: true));
        settings.KeyEncryptionCertificatePath = Path.Combine(Path.GetTempPath(), "identity.pfx");
        Assert.True(settings.IsValid(development: false));
        settings.KeyEncryptionCertificateBase64 = "encoded";
        Assert.False(settings.IsValid(development: false));
        settings.KeyEncryptionCertificatePath = "";
        Assert.True(settings.IsValid(development: false));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public void EnabledIdentityRejectsInvalidRateLimits(int limit) =>
        Assert.False(new NativeIdentitySettings { Enabled = true, RequestsPerMinute = limit }.IsValid(development: true));
}
