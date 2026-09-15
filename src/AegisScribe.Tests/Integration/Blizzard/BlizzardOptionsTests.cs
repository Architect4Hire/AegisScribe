using AegisScribe.Domain.Integration.Blizzard;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AegisScribe.Tests.Integration.Blizzard;

// 6.1 — configuration and host resolution (.claude/rules/external.md;
// references/blizzard-endpoints.md -> "Hosts").
public class BlizzardOptionsTests
{
    [Fact]
    public void DefaultHosts_AreTheRegionalOnes()
    {
        var options = new BlizzardOptions();

        Assert.Equal("https://us.api.blizzard.com/", options.ResolveApiBaseAddress().AbsoluteUri);
        Assert.Equal("https://us.battle.net/", options.ResolveOAuthBaseAddress().AbsoluteUri);
    }

    [Fact]
    public void DefaultOAuthHost_IsNotTheNonRegionalOne()
    {
        // external.md rules out oauth.battle.net: it resolves and reads like a sensible default, but it has
        // a documented history of intermittent 403s. This is the test that stops it drifting back in.
        Assert.DoesNotContain("oauth.battle.net", new BlizzardOptions().ResolveOAuthBaseAddress().AbsoluteUri);
    }

    [Theory]
    [InlineData("us", "https://us.api.blizzard.com/", "https://us.battle.net/")]
    [InlineData("eu", "https://eu.api.blizzard.com/", "https://eu.battle.net/")]
    [InlineData("kr", "https://kr.api.blizzard.com/", "https://kr.battle.net/")]
    [InlineData("tw", "https://tw.api.blizzard.com/", "https://tw.battle.net/")]
    [InlineData("sea", "https://sea.api.blizzard.com/", "https://sea.battle.net/")]
    public void EachSupportedRegion_ResolvesBothOfItsHosts(string region, string expectedApi, string expectedOAuth)
    {
        var options = new BlizzardOptions { Region = region };

        Assert.Equal(expectedApi, options.ResolveApiBaseAddress().AbsoluteUri);
        Assert.Equal(expectedOAuth, options.ResolveOAuthBaseAddress().AbsoluteUri);
    }

    [Fact]
    public void ConfiguredHostOverrides_WinOverTheRegionalDefaults()
    {
        // external.md wants the base address to come from config rather than a literal, so a regional outage
        // is a config change rather than a redeploy — and so tests can point at a stub.
        var options = new BlizzardOptions
        {
            ApiBaseAddress = "https://stub.example.com",
            OAuthBaseAddress = "https://stub-oauth.example.com",
        };

        Assert.Equal("https://stub.example.com/", options.ResolveApiBaseAddress().AbsoluteUri);
        Assert.Equal("https://stub-oauth.example.com/", options.ResolveOAuthBaseAddress().AbsoluteUri);
    }

    [Theory]
    [InlineData("us", "static-us", "dynamic-us", "profile-us")]
    [InlineData("EU", "static-eu", "dynamic-eu", "profile-eu")]
    public void Namespaces_CarryTheLowercasedRegion(
        string region,
        string expectedStatic,
        string expectedDynamic,
        string expectedProfile)
    {
        var options = new BlizzardOptions { Region = region };

        Assert.Equal(expectedStatic, options.StaticNamespace);
        Assert.Equal(expectedDynamic, options.DynamicNamespace);
        Assert.Equal(expectedProfile, options.ProfileNamespace);
    }

    [Fact]
    public void China_IsRejectedAsSeparateIntegration_NotAsAnotherRegion()
    {
        Assert.False(BlizzardHosts.IsSupported("cn"));

        var exception = Assert.Throws<InvalidOperationException>(() => BlizzardHosts.ApiHost("cn"));
        Assert.Contains("China", exception.Message);
    }

    [Fact]
    public void Validation_PassesWithNoCredentials()
    {
        // The regression test for the whole point of this step: a developer with no Blizzard credentials
        // must still be able to start the app. external.md — "Do not throw on startup; offline development
        // is a first-class case here."
        var options = Resolve([]);

        Assert.False(options.IsConfigured);
        Assert.Equal("us", options.Region);
    }

    [Fact]
    public void Validation_BindsCredentialsFromTheBlizzardSection()
    {
        // Matching the AppHost's Blizzard__ClientId / Blizzard__ClientSecret environment variables.
        var options = Resolve(new Dictionary<string, string?>
        {
            ["Blizzard:ClientId"] = "an-id",
            ["Blizzard:ClientSecret"] = "a-secret",
        });

        Assert.True(options.IsConfigured);
        Assert.Equal("an-id", options.ClientId);
    }

    [Fact]
    public void Validation_RejectsAnUnsupportedRegion()
    {
        // A region we have no hosts for is a genuine misconfiguration, unlike absent credentials — it should
        // stop startup rather than 404 on every call later.
        var exception = Assert.Throws<OptionsValidationException>(() =>
            Resolve(new Dictionary<string, string?> { ["Blizzard:Region"] = "moon" }));

        Assert.Contains("Blizzard:Region", exception.Message);
    }

    [Fact]
    public void Validation_RejectsANonPositiveRefreshSkew()
    {
        var exception = Assert.Throws<OptionsValidationException>(() =>
            Resolve(new Dictionary<string, string?> { ["Blizzard:TokenRefreshSkew"] = "00:00:00" }));

        Assert.Contains("TokenRefreshSkew", exception.Message);
    }

    private static BlizzardOptions Resolve(IEnumerable<KeyValuePair<string, string?>> configuration)
    {
        var services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder().AddInMemoryCollection(configuration).Build());

        services.AddBlizzardIntegration();

        using var provider = services.BuildServiceProvider();

        // Reading .Value is what runs the validators, which is the behaviour ValidateOnStart triggers at
        // startup.
        return provider.GetRequiredService<IOptions<BlizzardOptions>>().Value;
    }
}
