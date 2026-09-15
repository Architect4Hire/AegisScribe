using Microsoft.Extensions.Options;

namespace AegisScribe.SyncWorker;

// Runs the guild roster refresh at startup and then on the character pass's poll interval.
//
// No separate knob: both passes answer the same question — what has aged past the refresh window — and
// a second interval would be two names for one decision. The selection query is the gate, so a pass
// with nothing due costs one indexed query and stops.
public sealed class GuildRefreshWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<SyncWorkerOptions> options,
    TimeProvider timeProvider,
    ILogger<GuildRefreshWorker> logger) : BackgroundService
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
            using var scope = scopeFactory.CreateScope();

            await scope.ServiceProvider.GetRequiredService<GuildRefreshSync>().RunAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown, not a failure.
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The guild refresh pass failed. The next poll will retry.");
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
