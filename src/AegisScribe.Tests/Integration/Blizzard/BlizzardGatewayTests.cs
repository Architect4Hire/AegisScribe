using System.Net;
using AegisScribe.Domain.Integration.Blizzard;
using AegisScribe.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace AegisScribe.Tests.Integration.Blizzard;

// 6.1 — the gateway seam itself (.claude/rules/external.md -> "Blizzard — everything goes through the
// gateway"). The availability probe is what proves the whole chain: regional host, bearer header applied by
// BlizzardAuthHandler, and the namespace and locale every Blizzard request must carry.
public class BlizzardGatewayTests
{
    private const string ClientId = "test-client-id";
    private const string Secret = "test-client-secret-value";

    [Fact]
    public async Task CheckAvailability_WithNoCredentials_ReportsNotConfiguredAndCallsBlizzardNotAtAll()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, "{}");
        var gateway = CreateGateway(handler, out _, options =>
        {
            options.ClientId = null;
            options.ClientSecret = null;
        });

        Assert.Equal(BlizzardAvailability.NotConfigured, await gateway.CheckAvailabilityAsync(CancellationToken.None));
        Assert.Equal(0, handler.RequestCount);
        Assert.False(gateway.IsConfigured);
    }

    [Fact]
    public async Task CheckAvailability_WhenBlizzardAnswers_ReportsAvailable()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, """{"realms":[]}""");
        var gateway = CreateGateway(handler, out _);

        Assert.Equal(BlizzardAvailability.Available, await gateway.CheckAvailabilityAsync(CancellationToken.None));
        Assert.True(gateway.IsConfigured);
    }

    [Fact]
    public async Task CheckAvailability_SendsTheDynamicNamespaceAndLocale()
    {
        // Every Blizzard request carries both, as query parameters — locale is not a header, Accept-Language
        // does nothing. Realms move, so they are dynamic- data; a static- namespace here returns a 404 that
        // reads like "no realms exist", which references/blizzard-endpoints.md calls the most confusing
        // failure mode in this integration.
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, "{}");
        var gateway = CreateGateway(handler, out _);

        await gateway.CheckAvailabilityAsync(CancellationToken.None);

        Assert.Equal(
            "https://us.api.blizzard.com/data/wow/realm/index?namespace=dynamic-us&locale=en_US",
            handler.SingleRequest.AbsoluteUri);
    }

    [Fact]
    public async Task CheckAvailability_ForAnotherRegion_CarriesThatRegionsHostAndNamespace()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, "{}");
        var gateway = CreateGateway(handler, out _, options => options.Region = "eu");

        await gateway.CheckAvailabilityAsync(CancellationToken.None);

        Assert.Equal(
            "https://eu.api.blizzard.com/data/wow/realm/index?namespace=dynamic-eu&locale=en_US",
            handler.SingleRequest.AbsoluteUri);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task CheckAvailability_WhenBlizzardRefuses_ReportsUnavailableWithoutThrowing(HttpStatusCode statusCode)
    {
        var handler = StubHttpMessageHandler.Returning(statusCode);
        var gateway = CreateGateway(handler, out _);

        Assert.Equal(BlizzardAvailability.Unavailable, await gateway.CheckAvailabilityAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CheckAvailability_WhenBlizzardIsUnreachable_ReportsUnavailableWithoutThrowing()
    {
        var handler = new StubHttpMessageHandler((_, _) => throw new HttpRequestException("no route to host"));
        var gateway = CreateGateway(handler, out _);

        Assert.Equal(BlizzardAvailability.Unavailable, await gateway.CheckAvailabilityAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CheckAvailability_CalledRepeatedly_ProbesBlizzardOncePerCacheWindow()
    {
        // Health polling must not spend the hourly call budget — 36,000/hour is contractual
        // (blizzard-terms-and-limits.md), and an unbounded probe is the easy way to waste it on nothing.
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, "{}");
        var gateway = CreateGateway(handler, out var time);

        for (var i = 0; i < 20; i++)
        {
            Assert.Equal(BlizzardAvailability.Available, await gateway.CheckAvailabilityAsync(CancellationToken.None));
        }

        Assert.Equal(1, handler.RequestCount);

        time.Advance(BlizzardDefaults.AvailabilityCacheDuration + TimeSpan.FromSeconds(1));

        Assert.Equal(BlizzardAvailability.Available, await gateway.CheckAvailabilityAsync(CancellationToken.None));
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task CheckAvailability_AfterCredentialsStartWorking_RecoversOnceTheWindowPasses()
    {
        // A cached "unavailable" must not be permanent, or fixing credentials would need a restart.
        var handler = StubHttpMessageHandler.Scripted((_, attempt) => attempt <= 1
            ? StubHttpMessageHandler.Respond(HttpStatusCode.Unauthorized)
            : StubHttpMessageHandler.Respond(HttpStatusCode.OK, "{}"));

        var gateway = CreateGateway(handler, out var time);

        Assert.Equal(BlizzardAvailability.Unavailable, await gateway.CheckAvailabilityAsync(CancellationToken.None));

        time.Advance(BlizzardDefaults.AvailabilityCacheDuration + TimeSpan.FromSeconds(1));

        Assert.Equal(BlizzardAvailability.Available, await gateway.CheckAvailabilityAsync(CancellationToken.None));
    }

    private static BlizzardGateway CreateGateway(
        StubHttpMessageHandler handler,
        out FakeTimeProvider timeProvider,
        Action<BlizzardOptions>? configure = null)
    {
        var options = new BlizzardOptions
        {
            ClientId = ClientId,
            ClientSecret = Secret,
        };

        configure?.Invoke(options);

        timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2026-09-13T12:00:00Z"));

        // No BlizzardAuthHandler in this pipeline: the bearer header is its job and is covered by
        // BlizzardAuthHandlerTests. What these tests pin is the URL the gateway builds.
        var httpClient = new HttpClient(handler) { BaseAddress = options.ResolveApiBaseAddress() };

        return new BlizzardGateway(
            httpClient,
            new OptionsWrapper<BlizzardOptions>(options),
            new BlizzardAvailabilityCache(timeProvider),
            timeProvider,
            NullLogger<BlizzardGateway>.Instance);
    }
}
