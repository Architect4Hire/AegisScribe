using AegisScribe.Domain.Integration.Blizzard;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace AegisScribe.Tests.Integration.Blizzard;

// The Terms of Use permit storing Blizzard's data only if it is refreshed "no less frequently than
// every thirty (30) days", which makes the staleness window a contractual term wearing the costume of a
// cache setting — and therefore the setting most likely to be raised by somebody chasing fewer API
// calls. These tests are what stops that being a quiet change.
public class BlizzardStalenessOptionsTests
{
    [Fact]
    public void TheDefaultRefreshWindowIsWellInsideTheThirtyDayObligation()
    {
        var options = new BlizzardStalenessOptions();

        Assert.True(
            options.CharacterRefreshAfter <= BlizzardStalenessOptions.MaximumRefreshInterval,
            "The default character refresh window must not exceed the Terms of Use maximum.");

        // Not merely inside it. references/blizzard-terms-and-limits.md asks for margin on purpose:
        // refreshing at the deadline means a worker outage of a single day is already a breach, whereas
        // a weekly refresh leaves three weeks to notice and fix one.
        Assert.True(
            options.CharacterRefreshAfter <= TimeSpan.FromDays(14),
            "The default should leave real margin under 30 days, not sit at the deadline.");
    }

    [Fact]
    public void TheDefaultRealmRefreshWindowIsAlsoInsideTheObligation()
    {
        // Realms change rarely enough that this could sit much higher. It does not, because a stored
        // realm is Blizzard data and the thirty-day condition does not distinguish by how interesting
        // the rows are.
        var options = new BlizzardStalenessOptions();

        Assert.True(options.RealmRefreshAfter <= BlizzardStalenessOptions.MaximumRefreshInterval);
        Assert.True(options.RealmRefreshAfter <= TimeSpan.FromDays(14));
    }

    [Theory]
    [InlineData("31.00:00:00")]
    [InlineData("90.00:00:00")]
    public void AConfiguredRealmWindowLongerThanThirtyDaysStopsStartup(string configured)
    {
        var exception = Assert.Throws<OptionsValidationException>(
            () => Resolve(realmRefreshAfter: configured).RealmRefreshAfter);

        Assert.Contains("30", string.Join(" ", exception.Failures));
    }

    [Fact]
    public void TheTermsOfUseCeilingIsThirtyDays()
    {
        // Pinned so that "relaxing" the cap requires changing a test that says why it exists.
        Assert.Equal(TimeSpan.FromDays(30), BlizzardStalenessOptions.MaximumRefreshInterval);
    }

    [Theory]
    [InlineData("31.00:00:00")]
    [InlineData("60.00:00:00")]
    [InlineData("365.00:00:00")]
    public void AConfiguredWindowLongerThanThirtyDaysStopsStartup(string configured)
    {
        // ValidateOnStart, not a runtime check: a deployment configured to breach the Terms of Use must
        // fail to boot rather than serve traffic while quietly out of compliance.
        var exception = Assert.Throws<OptionsValidationException>(
            () => Resolve(configured).CharacterRefreshAfter);

        Assert.Contains("30", string.Join(" ", exception.Failures));
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("-1.00:00:00")]
    public void AZeroOrNegativeWindowStopsStartup(string configured)
    {
        // The opposite failure, and a rate-limit problem rather than a compliance one: every read would
        // be a Blizzard call, spending the contractual hourly budget on data already in SQL.
        Assert.Throws<OptionsValidationException>(() => Resolve(configured).CharacterRefreshAfter);
    }

    [Fact]
    public void AConfiguredWindowInsideTheCeilingIsAccepted()
    {
        Assert.Equal(TimeSpan.FromDays(21), Resolve("21.00:00:00").CharacterRefreshAfter);
    }

    [Fact]
    public void ThePolicyTreatsARowOlderThanTheWindowAsStale()
    {
        var now = DateTimeOffset.Parse("2026-09-14T12:00:00Z");
        var time = new FakeTimeProvider(now);
        var options = new BlizzardStalenessOptions { CharacterRefreshAfter = TimeSpan.FromDays(7) };
        var policy = new BlizzardStalenessPolicy(new OptionsWrapper<BlizzardStalenessOptions>(options), time);

        Assert.False(policy.IsStale(now));
        Assert.False(policy.IsStale(now.AddDays(-6)));
        Assert.True(policy.IsStale(now.AddDays(-8)));
        Assert.Equal(now.AddDays(-7), policy.StaleBefore());
    }

    // Goes through the real registration rather than newing the options up, so what is under test is the
    // validation that actually runs at startup.
    private static BlizzardStalenessOptions Resolve(
        string? characterRefreshAfter = null,
        string? realmRefreshAfter = null)
    {
        var settings = new Dictionary<string, string?>();

        if (characterRefreshAfter is not null)
        {
            settings["Blizzard:Staleness:CharacterRefreshAfter"] = characterRefreshAfter;
        }

        if (realmRefreshAfter is not null)
        {
            settings["Blizzard:Staleness:RealmRefreshAfter"] = realmRefreshAfter;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddBlizzardIntegration();

        return services.BuildServiceProvider()
            .GetRequiredService<IOptions<BlizzardStalenessOptions>>()
            .Value;
    }
}
