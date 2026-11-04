using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OrionSearch.Application.Research;
using OrionSearch.Infrastructure.Research;

namespace OrionSearch.Tests.Research;

public sealed class ResearchContractTests
{
    [Fact]
    public void WirePropertyNamesMustMatchTheSharedSchemaCasing()
    {
        Assert.Equal("https://xn--fa-hia.de/story", OrionSearch.Domain.Content.CanonicalUrl.Normalize("https://faß.de/story#fragment"));
        var body = JsonSerializer.Serialize(ResearchTestData.Output(Guid.NewGuid()), ResearchContract.Json)
            .Replace("\"contractVersion\"", "\"ContractVersion\"", StringComparison.Ordinal);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AgentResult>(body, ResearchContract.Json));
    }

    [Theory]
    [InlineData("120", 120)]
    [InlineData("100000", 300)]
    [InlineData("invalid", null)]
    public async Task RetryAfterIsParsedAndBoundedWithoutReadingPrivateFailureBodies(string header, int? delay)
    {
        using var http = new HttpClient(new Handler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("private body") };
            response.Headers.TryAddWithoutValidation("Retry-After", header);
            return response;
        }));
        var request = JsonSerializer.Deserialize<AgentRequest>(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Contracts/request.json")), ResearchContract.Json)!;
        var client = new HttpResearchAgentClient(http, new() { AgentBaseAddress = "https://agent.example", AgentApiKey = "test-service-key" });
        var failure = await Assert.ThrowsAsync<ResearchAgentException>(() => client.ExecuteAsync(request, default));
        Assert.Equal(ResearchFailure.Unavailable, failure.Failure);
        Assert.Equal(delay, failure.RetryAfterSeconds);
    }

    [Theory]
    [InlineData("text/html", false)]
    [InlineData("application/json", true)]
    public async Task SuccessfulBodiesMustUseJsonWithoutDuplicateFields(string mediaType, bool duplicate)
    {
        var request = JsonSerializer.Deserialize<AgentRequest>(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Contracts/request.json")), ResearchContract.Json)!;
        var body = JsonSerializer.Serialize(ResearchTestData.Output(request.ResearchRunId), ResearchContract.Json);
        if (duplicate) body = body[..^1] + ",\"contractVersion\":\"orion.research.v1\"}";
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK)
        { Content = new StringContent(body, System.Text.Encoding.UTF8, mediaType) }));
        var client = new HttpResearchAgentClient(http, new() { AgentBaseAddress = "https://agent.example", AgentApiKey = "test-service-key" });
        Assert.Equal(ResearchFailure.Contract, (await Assert.ThrowsAsync<ResearchAgentException>(() => client.ExecuteAsync(request, default))).Failure);
    }

    [Fact]
    public async Task PythonDiscoveryFixtureIsAcceptedWithoutInventingCapturesOrFindings()
    {
        var request = JsonSerializer.Deserialize<AgentRequest>(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Contracts/request.json")), ResearchContract.Json)!;
        var result = JsonSerializer.Deserialize<AgentResult>(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Contracts/discovery-result.json")), ResearchContract.Json)!;
        var accepted = ResearchOutputValidator.Normalize(result, request.ResearchRunId);
        Assert.Equal(ResearchContract.Version, accepted.ContractVersion);
        Assert.Single(accepted.Sources);
        Assert.Null(accepted.Sources[0].Capture);
        Assert.Null(accepted.Sources[0].PublishedAt);
        Assert.Empty(accepted.Findings);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProvisionalTransportRequiresExplicitOptInAndEchoedVersion(bool allowed)
    {
        var sent = false;
        var runId = Guid.NewGuid();
        using var http = new HttpClient(new Handler(_ =>
        {
            sent = true;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(ResearchTestData.Output(runId) with { ContractVersion = ResearchContract.ProvisionalVersion }, options: ResearchContract.Json)
            };
        }));
        var options = new ResearchWorkerOptions { AgentBaseAddress = "https://agent.example", AgentApiKey = "test-service-key", AllowProvisionalContract = allowed };
        var request = new AgentRequest(ResearchContract.ProvisionalVersion, runId, Guid.NewGuid(), "Topic", null, "Objective",
            new(null, null, [], [], [], JsonSerializer.SerializeToElement(new { }), DateTime.UtcNow));
        var client = new HttpResearchAgentClient(http, options);
        if (allowed) Assert.Equal(ResearchContract.ProvisionalVersion, (await client.ExecuteAsync(request, default)).ContractVersion);
        else Assert.Equal(ResearchFailure.Contract, (await Assert.ThrowsAsync<ResearchAgentException>(() => client.ExecuteAsync(request, default))).Failure);
        Assert.Equal(allowed, sent);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HttpSuccessCannotAcceptAnotherRunOrVersion(bool wrongRun)
    {
        var runId = Guid.NewGuid();
        var result = ResearchTestData.Output(wrongRun ? Guid.NewGuid() : runId) with
        { ContractVersion = wrongRun ? ResearchContract.Version : ResearchContract.ProvisionalVersion };
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = JsonContent.Create(result, options: ResearchContract.Json) }));
        var client = new HttpResearchAgentClient(http, new() { AgentBaseAddress = "https://agent.example", AgentApiKey = "test-service-key", AllowProvisionalContract = true });
        var request = new AgentRequest(ResearchContract.Version, runId, Guid.NewGuid(), "Topic", null, "Objective",
            new(null, null, [], [], [], JsonSerializer.SerializeToElement(new { }), DateTime.UtcNow));
        Assert.Equal(ResearchFailure.Contract, (await Assert.ThrowsAsync<ResearchAgentException>(() => client.ExecuteAsync(request, default))).Failure);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(send(request));
    }
}
