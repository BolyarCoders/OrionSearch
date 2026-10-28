using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrionSearch.Application.Research;
using OrionSearch.Tests.Archives;
using OrionSearch.Tests.Persistence;

namespace OrionSearch.Tests.Research;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class ResearchContractPersistenceTests(PostgreSqlFixture database)
{
    [Theory]
    [InlineData(120, 120)]
    [InlineData(int.MaxValue, 300)]
    [InlineData(-1, 10)]
    public async Task RetryAfterIsPersistedWithinTheConfiguredBackoffBudget(int hint, int expected)
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        await ResearchTestData.SubmitAsync(scenario);
        await using var scope = scenario.Scope();
        var queue = ResearchTestData.Queue(scope);
        var lease = (await queue.ClaimAsync("retry-worker", default))!;
        Assert.True(await queue.FailAsync(lease, ResearchFailure.Unavailable, default, hint));
        await using var context = scenario.Context();
        var job = await context.Jobs.SingleAsync();
        var attempt = await context.JobAttempts.SingleAsync();
        Assert.Equal(TimeSpan.FromSeconds(expected), job.RunAfter - attempt.FinishedAt!.Value);
        Assert.Equal("pending", job.Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExistingProvisionalPayloadIsPreservedAndOnlyDispatchedWithOptIn(bool allowed)
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        var run = await ResearchTestData.SubmitAsync(scenario);
        await using (var context = scenario.Context())
        {
            var job = await context.Jobs.SingleAsync();
            var payload = JsonSerializer.Deserialize<ResearchPayload>(job.Payload, ResearchContract.Json)!;
            job.Payload = JsonSerializer.Serialize(payload with { Request = payload.Request with { ContractVersion = ResearchContract.ProvisionalVersion } }, ResearchContract.Json);
            await context.SaveChangesAsync();
        }
        await using var scope = scenario.Scope();
        scope.ServiceProvider.GetRequiredService<ResearchWorkerOptions>().AllowProvisionalContract = allowed;
        var queue = ResearchTestData.Queue(scope);
        var lease = (await queue.ClaimAsync("compatibility-worker", default))!;
        var dispatched = await queue.DispatchAsync(lease, default);
        if (allowed)
        {
            Assert.Equal(ResearchContract.ProvisionalVersion, dispatched!.ContractVersion);
            Assert.True(await queue.CompleteAsync(lease, ResearchTestData.Output(run.Id) with { ContractVersion = ResearchContract.ProvisionalVersion }, default));
        }
        else Assert.Null(dispatched);
        await using var verify = scenario.Context();
        Assert.Contains(ResearchContract.ProvisionalVersion, (await verify.Jobs.SingleAsync()).Payload);
        Assert.Equal(allowed ? "completed" : "failed", (await verify.ResearchRuns.SingleAsync()).Status);
    }

    [Fact]
    public async Task CompletionCannotSwapVersionsEvenWhenBothVersionsAreEnabled()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        var run = await ResearchTestData.SubmitAsync(scenario);
        await using var scope = scenario.Scope();
        scope.ServiceProvider.GetRequiredService<ResearchWorkerOptions>().AllowProvisionalContract = true;
        var queue = ResearchTestData.Queue(scope);
        var lease = (await queue.ClaimAsync("version-worker", default))!;
        var rejected = await Assert.ThrowsAsync<ResearchAgentException>(() => queue.CompleteAsync(lease,
            ResearchTestData.Output(run.Id) with { ContractVersion = ResearchContract.ProvisionalVersion }, default));
        Assert.Equal(ResearchFailure.Contract, rejected.Failure);
        await using var context = scenario.Context();
        Assert.Empty(await context.Documents.ToListAsync());
        Assert.Equal("running", (await context.ResearchRuns.SingleAsync()).Status);
    }
}
