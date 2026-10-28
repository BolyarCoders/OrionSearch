using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrionSearch.Application.Research;
using OrionSearch.Tests.Archives;

namespace OrionSearch.Tests.Research;

internal static class ResearchTestData
{
    public static async Task<ResearchStatus> SubmitAsync(ArchiveApiScenario scenario, string? key = null, string? objective = "Research this topic")
    {
        using var client = scenario.Client();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/archives/{scenario.ArchiveId}/research-jobs") { Content = JsonContent.Create(new { objective }) };
        if (key is not null) request.Headers.Add("Idempotency-Key", key);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var status = (await response.Content.ReadFromJsonAsync<ResearchStatus>())!;
        Assert.Equal($"/research-jobs/{status.Id}", response.Headers.Location?.OriginalString);
        return status;
    }

    public static async Task ReadyAsync(ArchiveApiScenario scenario)
    {
        await using var context = scenario.Context();
        await context.Database.ExecuteSqlRawAsync("UPDATE system.jobs SET run_after = clock_timestamp() - interval '1 second' WHERE status = 'pending'");
    }

    public static async Task ExpireAsync(ArchiveApiScenario scenario)
    {
        await using var context = scenario.Context();
        await context.Database.ExecuteSqlRawAsync("UPDATE system.jobs SET locked_until = clock_timestamp() - interval '1 second' WHERE status = 'processing'");
    }

    public static AgentResult Output(Guid runId, string? url = null) => new(ResearchContract.Version, runId,
        [new QueryOutput("research methods", "test-provider")],
        [new SourceOutput("source-1", url ?? "HTTPS://EXAMPLE.COM:443/source#fragment", "Research source", "EN", null,
            new PublisherOutput("Example", " EXAMPLE.COM. "), 0.8m,
            new CaptureOutput(new DateTime(2026, 10, 18, 8, 0, 0, DateTimeKind.Utc), "Research preserves evidence.", null,
                [new ChunkOutput(0, "Research preserves evidence.", 0, 28)], JsonSerializer.SerializeToElement(new { producer = "test" })))],
        [new FindingOutput("finding-1", "A statement grounded in a source", "observation",
            [new EvidenceOutput("source-1", "supports", 0, "preserves evidence")], 0.7m)]);

    public static IResearchQueue Queue(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<IResearchQueue>();
}

internal sealed class FakeResearchAgent(Func<AgentRequest, CancellationToken, Task<AgentResult>> execute, bool configured = true) : IResearchAgentClient
{
    public int Calls { get; private set; }
    public bool IsConfigured => configured;
    public Task<AgentResult> ExecuteAsync(AgentRequest request, CancellationToken ct)
    {
        Calls++;
        return execute(request, ct);
    }
}
