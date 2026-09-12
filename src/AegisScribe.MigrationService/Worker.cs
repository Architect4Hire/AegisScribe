using AegisScribe.Domain.Data;
using Microsoft.EntityFrameworkCore;

namespace AegisScribe.MigrationService;

public class Worker(
    IServiceProvider serviceProvider,
    IHostApplicationLifetime hostApplicationLifetime,
    IConfiguration configuration,
    ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Migration service starting.");

        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AegisScribeDbContext>();

        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () => await db.Database.MigrateAsync(stoppingToken));

        await OpenIddictClientSeeder.SeedClientsAsync(scope.ServiceProvider, configuration);
        await DemoDataSeeder.SeedAsync(scope.ServiceProvider, logger);

        logger.LogInformation("Migration service finished.");
        hostApplicationLifetime.StopApplication();
    }
}
