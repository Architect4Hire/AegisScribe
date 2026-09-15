using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AegisScribe.Domain.Integration.Blizzard;

// Degraded, never Unhealthy. The API works without Blizzard — it serves stored data — so this must not
// be the reason a deployment is judged unready. The host registers it with the "external" tag, which
// keeps it out of /health and reports it at /health/external instead (ServiceDefaults ->
// MapDefaultEndpoints).
public sealed class BlizzardGatewayHealthCheck(IBlizzardGateway gateway) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var availability = await gateway.CheckAvailabilityAsync(cancellationToken);

        return availability switch
        {
            BlizzardAvailability.Available =>
                HealthCheckResult.Healthy("Blizzard is reachable and the configured credentials were accepted."),

            BlizzardAvailability.NotConfigured =>
                HealthCheckResult.Degraded(
                    "No Blizzard credentials are configured. Serving stored data only; set the " +
                    "blizzard-client-id and blizzard-client-secret Aspire parameters to enable live data."),

            _ => HealthCheckResult.Degraded(
                "Blizzard is configured but did not answer. Serving stored data only."),
        };
    }
}
