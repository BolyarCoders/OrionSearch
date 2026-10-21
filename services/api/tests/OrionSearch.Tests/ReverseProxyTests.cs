using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace OrionSearch.Tests;

public sealed class ReverseProxyTests
{
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
