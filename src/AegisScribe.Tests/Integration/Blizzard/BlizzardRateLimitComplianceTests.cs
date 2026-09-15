using AegisScribe.Domain.Integration.Blizzard;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AegisScribe.Tests.Integration.Blizzard;

// The contractual cap, pinned in a test. 36,000 calls per hour is a term of the Developer API Terms of
// Use rather than a performance tunable, and it is the setting most likely to be quietly raised by
// somebody chasing sync throughput — where raising it is a breach rather than a regression.
public class BlizzardRateLimitComplianceTests
{
    [Fact]
    public void TheDefaultSizing_StaysUnderTheContractualHourlyCap()
    {
        var options = new BlizzardOptions();

        var worstCase = BlizzardRateLimits.WorstCaseCallsPerHour(
            options.SustainedCallsPerSecond,
            options.BurstCallsPerSecond);

        Assert.True(
            worstCase <= BlizzardRateLimits.ContractualCallsPerHour,
            $"The default limiter allows {worstCase} calls/hour against a contractual cap of " +
            $"{BlizzardRateLimits.ContractualCallsPerHour}.");
    }

    [Fact]
    public void TheDefaultSizing_LeavesRealHeadroom()
    {
        // Not merely under the cap: under it with room to spare. Blizzard counts on their side, not ours, and
        // a second process could be sharing this client id — so sitting at 99.9% of the cap would be
        // technically compliant and practically a breach.
        var options = new BlizzardOptions();

        var worstCase = BlizzardRateLimits.WorstCaseCallsPerHour(
            options.SustainedCallsPerSecond,
            options.BurstCallsPerSecond);

        Assert.True(
            worstCase <= BlizzardRateLimits.ContractualCallsPerHour * 0.95,
            $"The default limiter allows {worstCase} calls/hour, leaving less than 5% headroom under the " +
            $"contractual {BlizzardRateLimits.ContractualCallsPerHour}.");
    }

    [Fact]
    public void TheDefaultBurst_StaysUnderThePerSecondCap()
    {
        // The hourly budget can look perfectly healthy while every call in a burst earns a 429.
        Assert.True(new BlizzardOptions().BurstCallsPerSecond <= BlizzardRateLimits.AssumedCallsPerSecond);
    }

    [Fact]
    public void Configuration_ThatWouldExceedTheHourlyCap_FailsValidation()
    {
        // 10/second is 36,000/hour exactly, and the bucket on top puts it over. Startup must refuse it.
        var exception = Assert.Throws<OptionsValidationException>(() =>
            Resolve(new Dictionary<string, string?>
            {
                ["Blizzard:SustainedCallsPerSecond"] = "10",
                ["Blizzard:BurstCallsPerSecond"] = "10",
            }));

        Assert.Contains("per hour", exception.Message);
    }

    [Fact]
    public void Configuration_ThatWouldExceedThePerSecondCap_FailsValidation()
    {
        var exception = Assert.Throws<OptionsValidationException>(() =>
            Resolve(new Dictionary<string, string?> { ["Blizzard:BurstCallsPerSecond"] = "500" }));

        Assert.Contains("per-second cap", exception.Message);
    }

    [Fact]
    public void Configuration_WithABucketSmallerThanOnePeriodsReplenishment_FailsValidation()
    {
        // A bucket that cannot hold one period's worth of tokens silently discards budget every second.
        var exception = Assert.Throws<OptionsValidationException>(() =>
            Resolve(new Dictionary<string, string?>
            {
                ["Blizzard:SustainedCallsPerSecond"] = "9",
                ["Blizzard:BurstCallsPerSecond"] = "4",
            }));

        Assert.Contains("BurstCallsPerSecond", exception.Message);
    }

    [Fact]
    public void ReducedSizing_IsAlwaysAllowed()
    {
        // Tightening is never a compliance question, so nothing should stand in its way.
        var options = Resolve(new Dictionary<string, string?>
        {
            ["Blizzard:SustainedCallsPerSecond"] = "2",
            ["Blizzard:BurstCallsPerSecond"] = "5",
        });

        Assert.Equal(2, options.SustainedCallsPerSecond);
        Assert.Equal(5, options.BurstCallsPerSecond);
    }

    private static BlizzardOptions Resolve(IEnumerable<KeyValuePair<string, string?>> configuration)
    {
        var services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder().AddInMemoryCollection(configuration).Build());

        services.AddBlizzardIntegration();

        using var provider = services.BuildServiceProvider();

        return provider.GetRequiredService<IOptions<BlizzardOptions>>().Value;
    }
}
