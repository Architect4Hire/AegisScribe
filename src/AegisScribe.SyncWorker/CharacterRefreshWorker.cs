using Microsoft.Extensions.Options;

namespace AegisScribe.SyncWorker;

// Runs the character refresh pass at startup and then on the configured poll.
//
// Unlike RealmCatalogueSyncWorker, there is no freshness gate to skip a pass: the selection query IS the
// gate. A run with nothing due costs one indexed query and stops, so a restart is already cheap without
// special handling.
public sealed class CharacterRefreshWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<SyncWorkerOptions> options,
    TimeProvider timeProvider,
    ILogger<CharacterRefreshWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.CharacterPollInterval, timeProvider);

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
            // CharacterRefreshSync opens its own scopes per unit of work — it has to, because its fan-out
            // cannot share a DbContext — so this only needs one to resolve the job itself.
            using var scope = scopeFactory.CreateScope();

            await scope.ServiceProvider.GetRequiredService<CharacterRefreshSync>().RunAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown, not a failure.
        }
        catch (Exception exception)
        {
            // A failed pass must not take the worker down. Rows stay stale, the next poll retries, and
            // the lazy refresh on the read path still covers anything somebody actually looks at.
            logger.LogError(exception, "The character refresh pass failed. The next poll will retry.");
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
