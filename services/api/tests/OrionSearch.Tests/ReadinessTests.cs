using System.Net;

namespace OrionSearch.Tests;

public sealed class ReadinessTests
{
    [Theory]
    [InlineData("")]
    [InlineData("Host=127.0.0.1;Port=1;Database=unavailable;Username=unused;Timeout=1;Command Timeout=1")]
    public async Task UnconfiguredOrUnavailableDatabaseDoesNotBreakLiveness(string connectionString)
    {
        using var factory = new ApiFactory();
        using var configuredFactory = factory.WithSettings(new() { ["ConnectionStrings:OrionSearch"] = connectionString });
        using var client = configuredFactory.CreateClient();

        using var liveness = await client.GetAsync("/health");
        using var readiness = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, liveness.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, readiness.StatusCode);
        Assert.Equal("Unhealthy", await readiness.Content.ReadAsStringAsync());
    }
}
