using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrionSearch.Infrastructure.Persistence;

namespace OrionSearch.Worker;

internal sealed class WorkerDatabaseCheck(IServiceScopeFactory scopes) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        const string failure = "Worker database is unavailable or its migration history is not current. No migrations were applied.";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<OrionSearchDbContext>();
            if (!await context.Database.CanConnectAsync(timeout.Token))
                throw new InvalidOperationException(failure);
            var applied = await context.Database.GetAppliedMigrationsAsync(timeout.Token);
            if (!context.Database.GetMigrations().SequenceEqual(applied))
                throw new InvalidOperationException(failure);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { throw new InvalidOperationException(failure); }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
