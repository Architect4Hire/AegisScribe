using AegisScribe.Domain.Integration.Blizzard;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;

namespace AegisScribe.Tests.Integration.Blizzard;

// 6.1 — the degraded-but-not-unhealthy report (.claude/rules/external.md: the gateway "reports itself
// unhealthy-but-degraded" when credentials are absent).
public class BlizzardGatewayHealthCheckTests
{
    [Fact]
    public async Task CheckHealth_WithNoCredentials_ReportsDegradedRatherThanUnhealthy()
    {
        // Unhealthy would be wrong and actively harmful: Aspire gates resource readiness on /health, and the
        // API genuinely works without Blizzard — it serves stored data. Reporting Unhealthy here would make
        // an offline `aspire run` look broken.
        var result = await CheckWith(BlizzardAvailability.NotConfigured);

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("No Blizzard credentials are configured", result.Description);
    }

    [Fact]
    public async Task CheckHealth_WhenBlizzardIsNotAnswering_ReportsDegraded()
    {
        var result = await CheckWith(BlizzardAvailability.Unavailable);

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("did not answer", result.Description);
    }

    [Fact]
    public async Task CheckHealth_WhenBlizzardIsReachable_ReportsHealthy()
    {
        var result = await CheckWith(BlizzardAvailability.Available);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    private static async Task<HealthCheckResult> CheckWith(BlizzardAvailability availability)
    {
        var gateway = Substitute.For<IBlizzardGateway>();
        gateway.CheckAvailabilityAsync(Arg.Any<CancellationToken>()).Returns(availability);

        var healthCheck = new BlizzardGatewayHealthCheck(gateway);

        return await healthCheck.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);
    }
}
