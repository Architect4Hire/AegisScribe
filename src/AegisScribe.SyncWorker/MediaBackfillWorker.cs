using Microsoft.Extensions.Options;

namespace AegisScribe.SyncWorker;

// Runs the media backfill at startup and then on its own poll — shorter than the character refresh's,
// because what it fills is what makes a character page look finished, and a newly seen item should not
// wait a quarter of an hour for its icon. A pass with nothing selected costs two indexed queries.
public sealed class MediaBackfillWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<SyncWorkerOptions> options,
    TimeProvider timeProvider,
    ILogger<MediaBackfillWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.MediaPollInterval, timeProvider);

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

            await scope.ServiceProvider.GetRequiredService<MediaBackfillSync>().RunAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown, not a failure.
        }
        catch (Exception exception)
        {
            // A failed pass must not take the worker down; the selection queries pick the rows up again.
            logger.LogError(exception, "The media backfill pass failed. The next poll will retry.");
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
