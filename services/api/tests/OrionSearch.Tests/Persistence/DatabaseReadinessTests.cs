using System.Net;

namespace OrionSearch.Tests.Persistence;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class DatabaseReadinessTests(PostgreSqlFixture database)
{
    [Fact]
    public async Task ReadinessReportsAReachablePostgreSqlDatabase()
    {
        using var factory = new ApiFactory();
        using var configuredFactory = factory.WithSettings(new() { ["ConnectionStrings:OrionSearch"] = database.ConnectionString });
        using var client = configuredFactory.CreateClient();

        using var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }
}
