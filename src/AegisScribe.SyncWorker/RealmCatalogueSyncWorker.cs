using AegisScribe.Domain.Integration.Blizzard;
using Microsoft.Extensions.Options;

namespace AegisScribe.SyncWorker;

// Runs the realm catalogue pass at startup and then on a poll.
//
// The poll interval is deliberately not the refresh window: RealmCatalogueSync decides whether a pass
// is due from what is stored, so this only controls how soon the worker notices. Putting the decision
// in the job rather than the schedule is what makes a restart cheap — the schedule has no memory
// across restarts, and the database does.
public sealed class RealmCatalogueSyncWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<RealmCatalogueSyncWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval, timeProvider);

        do
        {
            await RunOnceAsync(stoppingToken);
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            // A scope per pass: RealmCatalogueSync and the repository below it are scoped, because they
            // hold a DbContext. A BackgroundService is a singleton and must never capture one.
            using var scope = scopeFactory.CreateScope();
            var sync = scope.ServiceProvider.GetRequiredService<RealmCatalogueSync>();

            await sync.RunAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown, not a failure.
        }
        catch (Exception exception)
        {
            // A failed pass must not take the worker down with it. The catalogue is stale rather than
            // wrong, the next poll retries, and 6.4's lazy realm resolution still covers a lookup in the
            // meantime.
            logger.LogError(exception, "The realm catalogue sync pass failed. The next poll will retry.");
        }
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
