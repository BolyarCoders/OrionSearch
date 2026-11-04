using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrionSearch.Application.Research;

namespace OrionSearch.Infrastructure.Research;

public sealed class ResearchProcessor(IResearchQueue queue, IResearchAgentClient agent, ResearchWorkerOptions options, ILogger<ResearchProcessor> logger)
{
    public async Task<bool> ProcessOnceAsync(string workerId, CancellationToken ct = default)
    {
        if (!options.Enabled || !agent.IsConfigured) return false;
        await queue.RecoverAsync(ct);
        var lease = await queue.ClaimAsync(workerId, ct);
        if (lease is null) return false;
        var request = await queue.DispatchAsync(lease, ct);
        if (request is null) return true;
        using var call = CancellationTokenSource.CreateLinkedTokenSource(ct);
        call.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        using var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var pulse = KeepLeaseAsync(lease, heartbeat.Token, call);
        try
        {
            var result = await agent.ExecuteAsync(request, call.Token);
            var committed = await queue.CompleteAsync(lease, result, call.Token);
            logger.LogInformation("Research {RunId} attempt {Attempt} finished; committed={Committed}", lease.RunId, lease.Attempt, committed);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { await FailAsync(lease, ResearchFailure.Timeout, ct); }
        catch (ResearchAgentException exception) { await FailAsync(lease, exception.Failure, ct, exception.RetryAfterSeconds); }
        catch (Exception) { await FailAsync(lease, ResearchFailure.Persistence, ct); }
        finally
        {
            // Verification performs object I/O before completion locks the job. Keep
            // renewing through it; settle any blocked renewal after commit/rollback.
            await heartbeat.CancelAsync();
            await pulse;
        }
        return true;
    }

    private async Task FailAsync(ResearchLease lease, ResearchFailure failure, CancellationToken ct, int? retryAfterSeconds = null)
    {
        var recorded = await queue.FailAsync(lease, failure, ct, retryAfterSeconds);
        logger.LogWarning("Research {RunId} attempt {Attempt} failed: {Failure}; recorded={Recorded}", lease.RunId, lease.Attempt, failure, recorded);
    }

    private async Task KeepLeaseAsync(ResearchLease lease, CancellationToken stop, CancellationTokenSource call)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.HeartbeatSeconds));
        try
        {
            while (await timer.WaitForNextTickAsync(stop))
            {
                var renewed = await queue.HeartbeatAsync(lease, stop);
                if (stop.IsCancellationRequested) return;
                if (renewed) continue;
                await call.CancelAsync();
                return;
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception)
        {
            if (stop.IsCancellationRequested) return;
            logger.LogWarning("Research {RunId} attempt {Attempt} could not renew its lease", lease.RunId, lease.Attempt);
            await call.CancelAsync();
        }
    }
}

internal sealed class ResearchBackgroundService(IServiceScopeFactory scopes, ResearchWorkerOptions options, ILogger<ResearchBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled) return;
        var workerId = Guid.NewGuid().ToString("N");
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.PollSeconds));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var agent = scope.ServiceProvider.GetRequiredService<IResearchAgentClient>();
                if (!agent.IsConfigured)
                {
                    logger.LogWarning("Research processing is enabled but the agent is unconfigured; no jobs will be claimed");
                    return;
                }
                var processor = scope.ServiceProvider.GetRequiredService<ResearchProcessor>();
                while (await processor.ProcessOnceAsync(workerId, stoppingToken)) { }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception) { logger.LogWarning("Research queue processing failed; retrying on the next poll"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
