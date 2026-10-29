using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace OrionSearch.Tests;

public sealed class ReverseProxyTests
{
    [Theory]
    [InlineData("/auth/login")]
    [InlineData("/archives/11111111-1111-1111-1111-111111111111/research-jobs")]
    public async Task RenderEdgeRateLimitsClientsIndependentlyAndIgnoresForwardedFor(string path)
    {
        using var factory = new ApiFactory("Development");
        using var configured = factory.WithProbe(IPAddress.Parse("10.0.0.5"), new()
        {
            ["ReverseProxy:TrustRenderEdge"] = "true",
            ["RENDER"] = "true",
            ["RENDER_SERVICE_TYPE"] = "web",
            ["NativeIdentity:Enabled"] = "true",
            ["NativeIdentity:RequestsPerMinute"] = "1",
            ["ResearchLimits:MaxSubmissionsPerIpPerMinute"] = "1"
        });
        using var client = configured.CreateClient();
        async Task<HttpStatusCode> Send(string edgeIp, string spoofed)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(new { }) };
            request.Headers.Add("CF-Connecting-IP", edgeIp);
            request.Headers.Add("X-Forwarded-For", spoofed);
            using var result = await client.SendAsync(request);
            return result.StatusCode;
        }
        Assert.NotEqual(HttpStatusCode.TooManyRequests, await Send("203.0.113.10", "192.0.2.1"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await Send("203.0.113.10", "192.0.2.2"));
        Assert.NotEqual(HttpStatusCode.TooManyRequests, await Send("203.0.113.11", "192.0.2.1"));
    }

    [Fact]
    public async Task DirectDeploymentDoesNotTrustClientSuppliedCloudflareHeaders()
    {
        using var factory = new ApiFactory("Development");
        using var configured = factory.WithProbe(IPAddress.Parse("192.0.2.10"), new()
        {
            ["NativeIdentity:Enabled"] = "true",
            ["NativeIdentity:RequestsPerMinute"] = "1"
        });
        using var client = configured.CreateClient();
        async Task<HttpStatusCode> Send(string spoofed)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/login") { Content = JsonContent.Create(new { }) };
            request.Headers.Add("CF-Connecting-IP", spoofed);
            using var response = await client.SendAsync(request);
            return response.StatusCode;
        }
        Assert.NotEqual(HttpStatusCode.TooManyRequests, await Send("203.0.113.1"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await Send("203.0.113.2"));
    }

    [Theory]
    [InlineData("false", "web")]
    [InlineData("true", "pserv")]
    public void RenderEdgeModeFailsStartupOutsideRenderWebServices(string render, string serviceType)
    {
        using var factory = new ApiFactory("Development");
        using var configured = factory.WithSettings(new() { ["ReverseProxy:TrustRenderEdge"] = "true", ["RENDER"] = render, ["RENDER_SERVICE_TYPE"] = serviceType });
        Assert.Throws<Microsoft.Extensions.Options.OptionsValidationException>(() => configured.CreateClient());
    }

    [Fact]
    public async Task MissingOrMalformedRenderAddressesShareAConservativeBucket()
    {
        using var factory = new ApiFactory("Development");
        using var configured = factory.WithProbe(IPAddress.Parse("10.0.0.5"), new()
        {
            ["ReverseProxy:TrustRenderEdge"] = "true",
            ["RENDER"] = "true",
            ["RENDER_SERVICE_TYPE"] = "web",
            ["ResearchLimits:MaxSubmissionsPerIpPerMinute"] = "1"
        });
        using var client = configured.CreateClient();
        var path = "/archives/11111111-1111-1111-1111-111111111111/research-jobs";
        using var missing = await client.PostAsJsonAsync(path, new { });
        Assert.NotEqual(HttpStatusCode.TooManyRequests, missing.StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(new { }) };
        request.Headers.Add("CF-Connecting-IP", "203.0.113.10, 203.0.113.11");
        using var malformed = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.TooManyRequests, malformed.StatusCode);
    }

    [Theory]
    [InlineData("10.0.0.5", "https", "203.0.113.10")]
    [InlineData("10.0.0.6", "http", "10.0.0.6")]
    public async Task ForwardedHeadersAreOnlyAcceptedFromTrustedProxies(
        string proxyAddress, string expectedScheme, string expectedClientAddress)
    {
        using var factory = new ApiFactory();
        using var probeFactory = factory.WithProbe(IPAddress.Parse(proxyAddress),
            new() { ["ReverseProxy:KnownProxies:0"] = "10.0.0.5" });
        using var client = probeFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/__test/request");
        request.Headers.Add("X-Forwarded-For", "203.0.113.10");
        request.Headers.Add("X-Forwarded-Proto", "https");

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedScheme, body.GetProperty("scheme").GetString());
        Assert.Equal(expectedClientAddress, body.GetProperty("remoteIp").GetString());
    }
}
