using System.Net;
using Microsoft.Extensions.Options;

namespace OrionSearch.Tests;

public sealed class ConfigurationTests
{
    [Theory]
    [InlineData("Development", "http://localhost:3000", true)]
    [InlineData("Development", "https://untrusted.example", false)]
    [InlineData("Production", "http://localhost:3000", false)]
    public async Task CorsRespectsEnvironmentDefaults(string environment, string origin, bool allowed)
    {
        using var factory = new ApiFactory(environment);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", origin);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(allowed, response.Headers.Contains("Access-Control-Allow-Origin"));
        if (allowed)
        {
            Assert.Equal(origin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        }
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task ConfiguredOriginCanMakePreflightRequests()
    {
        const string origin = "https://web.example";
        using var factory = new ApiFactory();
        using var configuredFactory = factory.WithSettings(new() { ["Cors:AllowedOrigins:0"] = origin });
        using var client = configuredFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/health");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(origin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Contains("GET", response.Headers.GetValues("Access-Control-Allow-Methods"));
        Assert.True(response.Headers.Contains("Access-Control-Allow-Headers"));
    }

    [Theory]
    [InlineData("Cors:AllowedOrigins:0", "*")]
    [InlineData("Cors:AllowedOrigins:0", "https://web.example/path")]
    [InlineData("Cors:AllowedOrigins:0", "https://web.example/")]
    [InlineData("Cors:AllowedOrigins:0", "ftp://web.example")]
    [InlineData("ReverseProxy:KnownProxies:0", "not-an-ip")]
    public void InvalidConfigurationFailsAtStartup(string key, string value)
    {
        using var factory = new ApiFactory();
        using var configuredFactory = factory.WithSettings(new() { [key] = value });

        Assert.Throws<OptionsValidationException>(() => configuredFactory.CreateClient());
    }
}
