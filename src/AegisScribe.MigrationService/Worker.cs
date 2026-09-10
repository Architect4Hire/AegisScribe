namespace AegisScribe.MigrationService;

public class Worker(IHostApplicationLifetime hostApplicationLifetime, ILogger<Worker> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Migration service starting.");

        // AegisScribeDbContext doesn't exist yet. The execution-strategy migration call
        // (.claude/rules/backend.md) attaches here once it does — this step is the
        // run-once-and-exit host loop only.

        hostApplicationLifetime.StopApplication();
        return Task.CompletedTask;
    }
}
