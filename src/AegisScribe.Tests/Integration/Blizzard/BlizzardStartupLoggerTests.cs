using AegisScribe.Domain.Integration.Blizzard;
using AegisScribe.Tests.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AegisScribe.Tests.Integration.Blizzard;

// 6.1 — external.md: the integration "logs once at startup" when credentials are absent.
//
// This exists because the obvious home for that warning does not work. BlizzardGateway returns on
// IsConfigured before the token provider is ever asked, so a warning inside BlizzardTokenProvider is
// unreachable on the one machine that needs it. Found by running the app and finding no such line in the
// API log; these tests are what stop it regressing back to that.
public class BlizzardStartupLoggerTests
{
    private const string Id = "an-id";
    private const string Secret = "a-secret";

    [Fact]
    public async Task Startup_WithNoCredentials_WarnsOnce()
    {
        var logger = new CapturingLogger<BlizzardStartupLogger>();
        var startupLogger = Create(logger, configured: false);

        await startupLogger.StartAsync(CancellationToken.None);

        var warning = Assert.Single(logger.WithLevel(LogLevel.Warning));
        Assert.Contains("No Blizzard credentials are configured", warning.Message);
        Assert.Contains("blizzard-client-id", warning.Message);
    }

    [Fact]
    public async Task Startup_WithCredentials_ReportsTheRegionAtInformation_AndDoesNotWarn()
    {
        var logger = new CapturingLogger<BlizzardStartupLogger>();
        var startupLogger = Create(logger, configured: true, options => options.Region = "eu");

        await startupLogger.StartAsync(CancellationToken.None);

        Assert.Empty(logger.WithLevel(LogLevel.Warning));

        var info = Assert.Single(logger.WithLevel(LogLevel.Information));
        Assert.Contains("eu", info.Message);
        Assert.Contains("https://eu.api.blizzard.com/", info.Message);
    }

    [Fact]
    public void AddBlizzardIntegration_RegistersTheStartupLoggerAsAHostedService()
    {
        // The warning is only delivered if the registration actually wires it up, and nothing else in the
        // suite would notice if that line were dropped.
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        services.AddBlizzardIntegration();

        using var provider = services.BuildServiceProvider();

        Assert.Contains(provider.GetServices<IHostedService>(), service => service is BlizzardStartupLogger);
    }

    private static BlizzardStartupLogger Create(
        CapturingLogger<BlizzardStartupLogger> logger,
        bool configured,
        Action<BlizzardOptions>? configure = null)
    {
        var options = new BlizzardOptions();

        if (configured)
        {
            options.ClientId = Id;
            options.ClientSecret = Secret;
        }

        configure?.Invoke(options);

        return new BlizzardStartupLogger(new OptionsWrapper<BlizzardOptions>(options), logger);
    }
}
