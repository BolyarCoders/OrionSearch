using System.Net;
using System.Net.Http.Json;
using OrionSearch.Application.Archives;
using OrionSearch.Application.Research;
using OrionSearch.Infrastructure.Research;

namespace OrionSearch.Tests.Research;

public sealed class ResearchAgentHttpTests
{
    [Fact]
    public async Task HttpClientUsesOnlyConfiguredAddressAuthenticationAndVersionedBody()
    {
        var request = Request();
        var options = Options();
        using var client = new HttpClient(new StubHandler(async (message, _) =>
        {
            Assert.Equal("https://agent.example/internal/research", message.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", message.Headers.Authorization!.Scheme);
            Assert.Equal(options.AgentApiKey, message.Headers.Authorization.Parameter);
            var sent = await message.Content!.ReadFromJsonAsync<AgentRequest>(ResearchContract.Json);
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(request, ResearchContract.Json), System.Text.Json.JsonSerializer.Serialize(sent, ResearchContract.Json));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(ResearchTestData.Output(request.ResearchRunId), options: ResearchContract.Json) };
        }));
        var result = await new HttpResearchAgentClient(client, options).ExecuteAsync(request, default);
        Assert.Equal(request.ResearchRunId, result.ResearchRunId);
    }

    [Theory]
    [InlineData(401, ResearchFailure.Authentication)]
    [InlineData(403, ResearchFailure.Authentication)]
    [InlineData(429, ResearchFailure.Unavailable)]
    [InlineData(503, ResearchFailure.Unavailable)]
    [InlineData(302, ResearchFailure.Contract)]
    [InlineData(400, ResearchFailure.Contract)]
    public async Task HttpFailuresUseFiniteSafeCategories(int code, ResearchFailure failure)
    {
        using var client = new HttpClient(new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)code)
        { Content = new StringContent("private credential-bearing error body") })));
        var exception = await Assert.ThrowsAsync<ResearchAgentException>(() => new HttpResearchAgentClient(client, Options()).ExecuteAsync(Request(), default));
        Assert.Equal(failure, exception.Failure);
        Assert.DoesNotContain("private", exception.Message);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not-json")]
    [InlineData("null")]
    public async Task InvalidBodiesCannotBecomeSuccessfulResults(string body)
    {
        using var client = new HttpClient(new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") })));
        var exception = await Assert.ThrowsAsync<ResearchAgentException>(() => new HttpResearchAgentClient(client, Options()).ExecuteAsync(Request(), default));
        Assert.Equal(ResearchFailure.Contract, exception.Failure);
    }

    [Fact]
    public async Task ResponseSizeIsBoundedEvenWithoutAContentLengthHeader()
    {
        var options = Options();
        options.MaxResponseBytes = 1024;
        using var client = new HttpClient(new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = JsonStream(new NonSeekableMemoryStream(new byte[1025])) })));
        var exception = await Assert.ThrowsAsync<ResearchAgentException>(() => new HttpResearchAgentClient(client, options).ExecuteAsync(Request(), default));
        Assert.Equal(ResearchFailure.Contract, exception.Failure);
    }

    [Fact]
    public async Task NetworkFailuresRetryAndCallerCancellationIsPreserved()
    {
        using var failed = new HttpClient(new StubHandler((_, _) => throw new HttpRequestException("private network address")));
        Assert.Equal(ResearchFailure.Unavailable, (await Assert.ThrowsAsync<ResearchAgentException>(() => new HttpResearchAgentClient(failed, Options()).ExecuteAsync(Request(), default))).Failure);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new HttpResearchAgentClient(failed, Options()).ExecuteAsync(Request(), cancellation.Token));
    }

    [Theory]
    [InlineData("http://public-agent.example/")]
    [InlineData("https://name:credential@agent.example/")]
    [InlineData("https://agent.example/?credential=secret")]
    [InlineData("file:///private/path")]
    public void UnsafeAgentAddressesAreRejected(string address)
    {
        Assert.False(ResearchWorkerOptions.ValidAddress(address));
    }

    [Fact]
    public async Task AnInterruptedResponseBodyIsATransientServiceFailure()
    {
        using var http = new HttpClient(new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = JsonStream(new InterruptedResponseStream()) })));
        var failure = await Assert.ThrowsAsync<ResearchAgentException>(() => new HttpResearchAgentClient(http, Options()).ExecuteAsync(Request(), default));
        Assert.Equal(ResearchFailure.Unavailable, failure.Failure);
        Assert.DoesNotContain("private", failure.Message);
    }

    [Fact]
    public void StableConfigurationRequiresCredentialsWithoutProvisionalOptIn()
    {
        using var http = new HttpClient();
        var options = Options();
        options.AllowProvisionalContract = false;
        Assert.True(new HttpResearchAgentClient(http, options).IsConfigured);
        options.AllowProvisionalContract = true;
        options.AgentApiKey = null;
        Assert.False(new HttpResearchAgentClient(http, options).IsConfigured);
        Assert.False(new ResearchWorkerOptions { HeartbeatSeconds = 100 }.IsValid());
    }

    [Theory]
    [InlineData("contains space")]
    [InlineData("contains\r\nnewline")]
    public void MalformedCredentialsLeaveTheAgentUnconfigured(string credential)
    {
        using var http = new HttpClient();
        var options = Options();
        options.AgentApiKey = credential;
        Assert.False(new HttpResearchAgentClient(http, options).IsConfigured);
    }

    private static StreamContent JsonStream(Stream stream)
    {
        var content = new StreamContent(stream);
        content.Headers.ContentType = new("application/json");
        return content;
    }

    private static ResearchWorkerOptions Options() => new()
    {
        AgentBaseAddress = "https://agent.example/internal/",
        AgentApiKey = "synthetic-test-service-credential",
        AllowProvisionalContract = true
    };
    private static AgentRequest Request() => new(ResearchContract.Version, Guid.NewGuid(), Guid.NewGuid(), "Topic", null, "Objective",
        new ArchiveSettingsData(null, null, [], [], [], System.Text.Json.JsonSerializer.SerializeToElement(new { }), DateTime.UtcNow));
    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
    private sealed class NonSeekableMemoryStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
    private sealed class InterruptedResponseStream() : MemoryStream
    {
        public override bool CanSeek => false;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new IOException("private upstream connection reset"));
    }
}
