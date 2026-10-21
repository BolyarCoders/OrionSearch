using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace OrionSearch.Tests;

public sealed class ApiTests
{
    [Fact]
    public void ApplicationStartsWithoutExternalServices()
    {
        using var factory = new ApiFactory();

        var lifetime = factory.Services.GetRequiredService<IHostApplicationLifetime>();

        Assert.True(lifetime.ApplicationStarted.IsCancellationRequested);
        Assert.False(lifetime.ApplicationStopping.IsCancellationRequested);
    }

    [Fact]
    public async Task HealthReturnsHealthy()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("healthy", body.GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task UnexpectedErrorsReturnProblemDetailsWithoutExceptionText(string environment)
    {
        using var factory = new ApiFactory(environment);
        using var probeFactory = factory.WithProbe();
        using var client = probeFactory.CreateClient();

        using var response = await client.GetAsync("/__test/throw");
        var body = await response.Content.ReadAsStringAsync();
        var problem = JsonSerializer.Deserialize<ProblemDetails>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(problem);
        Assert.Equal(500, problem.Status);
        Assert.Equal("An unexpected error occurred.", problem.Title);
        Assert.Equal("/__test/throw", problem.Instance);
        Assert.Null(problem.Detail);
        var traceId = Assert.IsType<JsonElement>(problem.Extensions["traceId"]).GetString();
        Assert.False(string.IsNullOrWhiteSpace(traceId));
        Assert.DoesNotContain("Sensitive exception text", body);
        Assert.DoesNotContain("InvalidOperationException", body);
    }

    [Fact]
    public async Task UnknownRoutesReturnProblemDetails()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/__test/throw");
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(404, problem?.Status);
    }

    [Theory]
    [InlineData("Development", HttpStatusCode.OK)]
    [InlineData("Production", HttpStatusCode.NotFound)]
    public async Task OpenApiIsOnlyAvailableInDevelopment(string environment, HttpStatusCode expectedStatus)
    {
        using var factory = new ApiFactory(environment);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(expectedStatus, response.StatusCode);
        if (expectedStatus == HttpStatusCode.OK)
        {
            var document = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(document.GetProperty("paths").TryGetProperty("/health", out _));
        }
    }
}
