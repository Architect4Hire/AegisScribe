using System.Net;
using System.Net.Http.Headers;
using System.Threading.RateLimiting;
using AegisScribe.Domain.Integration.Blizzard;
using AegisScribe.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace AegisScribe.Tests.Integration.Blizzard;

// 6.2 — the handler that makes "every gateway method takes a lease" structural rather than a convention, and
// that turns Blizzard's Retry-After into a shared backoff window.
public class BlizzardRateLimitHandlerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-13T12:00:00Z");

    [Fact]
    public async Task Send_TakesALeaseForEveryCall_AndReleasesIt()
    {
        var lease = FakeRateLimitLease.Acquired();
        var inner = StubHttpMessageHandler.Returning(HttpStatusCode.OK, "{}");
        var client = CreateClient(inner, lease, out var rateLimiter);

        await client.GetAsync("/data/wow/realm/index?namespace=dynamic-us&locale=en_US");

        await rateLimiter.Received(1).AcquireAsync(Arg.Any<CancellationToken>());
        Assert.Equal(1, inner.RequestCount);

        // A leaked lease is invisible until the budget quietly runs dry.
        Assert.True(lease.WasDisposed);
    }

    [Fact]
    public async Task Send_WhenTheLeaseIsRefused_SendsNothingAndReportsUnavailable()
    {
        // The budget is spent or we are inside a backoff window. Short-circuiting is the point: the call that
        // would have earned a 429 is never made.
        var inner = StubHttpMessageHandler.Returning(HttpStatusCode.OK, "{}");
        var client = CreateClient(inner, FakeRateLimitLease.Denied(), out _);

        var response = await client.GetAsync("/data/wow/realm/index?namespace=dynamic-us&locale=en_US");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(0, inner.RequestCount);
    }

    [Fact]
    public async Task Send_OnASuccessfulResponse_RegistersNoBackoff()
    {
        var inner = StubHttpMessageHandler.Returning(HttpStatusCode.OK, "{}");
        var client = CreateClient(inner, FakeRateLimitLease.Acquired(), out var rateLimiter);

        await client.GetAsync("/data/wow/realm/index?namespace=dynamic-us&locale=en_US");

        rateLimiter.DidNotReceiveWithAnyArgs().RegisterThrottled(default);
    }

    [Fact]
    public async Task Send_On429WithDeltaSecondsRetryAfter_BacksOffForThatLong()
    {
        // The form Blizzard actually sends.
        var inner = new StubHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(42));
            return response;
        });

        var client = CreateClient(inner, FakeRateLimitLease.Acquired(), out var rateLimiter);

        var response = await client.GetAsync("/data/wow/realm/index?namespace=dynamic-us&locale=en_US");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        rateLimiter.Received(1).RegisterThrottled(TimeSpan.FromSeconds(42));
    }

    [Fact]
    public async Task Send_On429WithAnHttpDateRetryAfter_BacksOffUntilThatMoment()
    {
        // The date form is legal per RFC 9110 and a proxy in the path may rewrite the delta into one, so both
        // are read rather than only the one Blizzard happens to send today.
        var inner = new StubHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(Now.AddSeconds(30));
            return response;
        });

        var client = CreateClient(inner, FakeRateLimitLease.Acquired(), out var rateLimiter);

        await client.GetAsync("/data/wow/realm/index?namespace=dynamic-us&locale=en_US");

        rateLimiter.Received(1).RegisterThrottled(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Send_On429WithARetryAfterDateInThePast_BacksOffForNoTime()
    {
        // "You may retry now", not "wait forever".
        var inner = new StubHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(Now.AddSeconds(-30));
            return response;
        });

        var client = CreateClient(inner, FakeRateLimitLease.Acquired(), out var rateLimiter);

        await client.GetAsync("/data/wow/realm/index?namespace=dynamic-us&locale=en_US");

        rateLimiter.Received(1).RegisterThrottled(TimeSpan.Zero);
    }

    [Fact]
    public async Task Send_On429WithNoRetryAfter_LetsTheLimiterChooseTheBackoff()
    {
        var inner = StubHttpMessageHandler.Returning(HttpStatusCode.TooManyRequests);
        var client = CreateClient(inner, FakeRateLimitLease.Acquired(), out var rateLimiter);

        await client.GetAsync("/data/wow/realm/index?namespace=dynamic-us&locale=en_US");

        rateLimiter.Received(1).RegisterThrottled(null);
    }

    [Fact]
    public async Task Send_ThroughTheAuthHandlerAndTheRateLimiter_AppliesBothInThatOrder()
    {
        // The composition the registration builds: auth outside, rate limiter innermost. Auth first so an
        // unconfigured deployment short-circuits without spending budget; the limiter last so it sits
        // immediately before the wire.
        var inner = StubHttpMessageHandler.Returning(HttpStatusCode.OK, "{}");
        var rateLimiter = Substitute.For<IBlizzardRateLimiter>();
        rateLimiter.AcquireAsync(Arg.Any<CancellationToken>()).Returns(FakeRateLimitLease.Acquired());

        var tokenProvider = Substitute.For<IBlizzardTokenProvider>();
        tokenProvider.GetTokenAsync(Arg.Any<CancellationToken>()).Returns("token-one");

        var rateLimitHandler = new BlizzardRateLimitHandler(
            rateLimiter,
            new FakeTimeProvider(Now),
            NullLogger<BlizzardRateLimitHandler>.Instance)
        {
            InnerHandler = inner,
        };

        var authHandler = new BlizzardAuthHandler(tokenProvider) { InnerHandler = rateLimitHandler };

        using var client = new HttpClient(authHandler) { BaseAddress = new Uri("https://us.api.blizzard.com") };

        await client.GetAsync("/data/wow/realm/index?namespace=dynamic-us&locale=en_US");

        Assert.Equal("Bearer", inner.SingleRequest.AuthorizationScheme);
        await rateLimiter.Received(1).AcquireAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Send_WithNoCredentials_NeverReachesTheRateLimiter()
    {
        // The reason auth sits outside: discovering that we have no credentials must not cost a token from a
        // budget that is contractually capped.
        var inner = StubHttpMessageHandler.Returning(HttpStatusCode.OK, "{}");
        var rateLimiter = Substitute.For<IBlizzardRateLimiter>();

        var tokenProvider = Substitute.For<IBlizzardTokenProvider>();
        tokenProvider.GetTokenAsync(Arg.Any<CancellationToken>()).Returns((string?)null);

        var rateLimitHandler = new BlizzardRateLimitHandler(
            rateLimiter,
            new FakeTimeProvider(Now),
            NullLogger<BlizzardRateLimitHandler>.Instance)
        {
            InnerHandler = inner,
        };

        var authHandler = new BlizzardAuthHandler(tokenProvider) { InnerHandler = rateLimitHandler };

        using var client = new HttpClient(authHandler) { BaseAddress = new Uri("https://us.api.blizzard.com") };

        var response = await client.GetAsync("/data/wow/realm/index?namespace=dynamic-us&locale=en_US");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(0, inner.RequestCount);
        await rateLimiter.DidNotReceiveWithAnyArgs().AcquireAsync(default);
    }

    private static HttpClient CreateClient(
        StubHttpMessageHandler inner,
        RateLimitLease lease,
        out IBlizzardRateLimiter rateLimiter)
    {
        rateLimiter = Substitute.For<IBlizzardRateLimiter>();
        rateLimiter.AcquireAsync(Arg.Any<CancellationToken>()).Returns(lease);

        var handler = new BlizzardRateLimitHandler(
            rateLimiter,
            new FakeTimeProvider(Now),
            NullLogger<BlizzardRateLimitHandler>.Instance)
        {
            InnerHandler = inner,
        };

        return new HttpClient(handler) { BaseAddress = new Uri("https://us.api.blizzard.com") };
    }
}
