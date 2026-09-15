using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AegisScribe.Domain.Integration.Blizzard;

// Says once, at startup, that the integration has no credentials (external.md).
//
// A hosted service rather than a log inside the token provider, because BlizzardGateway checks
// IsConfigured and returns before a token is ever requested — so that warning is never reached on
// precisely the machine that needs it. This fires regardless of whether anything asks Blizzard for
// anything.
public sealed class BlizzardStartupLogger(
    IOptions<BlizzardOptions> options,
    ILogger<BlizzardStartupLogger> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;

        if (settings.IsConfigured)
        {
            logger.LogInformation(
                "The Blizzard integration is configured for region {Region} ({ApiBaseAddress}), locale {Locale}.",
                settings.Region,
                settings.ResolveApiBaseAddress(),
                settings.Locale);
        }
        else
        {
            logger.LogWarning(
                "No Blizzard credentials are configured, so Blizzard-backed data is unavailable and this service is " +
                "running on stored data only. Set the blizzard-client-id and blizzard-client-secret Aspire parameters " +
                "(user secrets on the AppHost) to enable it.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
