using System.Net;
using System.Text;
using AegisScribe.Domain.Integration.Blizzard;
using AegisScribe.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace AegisScribe.Tests.Integration.Blizzard;

// 6.1 — the client-credentials token lifecycle (.claude/rules/external.md -> "Blizzard — everything goes
// through the gateway"; add-external-sync skill).
//
// The behaviours under test are the four the rule names: cache the token, refresh once under a lock, send
// it as a bearer header and never as a query parameter, and degrade rather than throw when credentials
// are absent.
public class BlizzardTokenProviderTests
{
    private const string ClientId = "test-client-id";
    private const string Secret = "test-client-secret-value";

    [Fact]
    public async Task GetToken_PostsToTheRegionalOAuthHost_WithBasicCredentialsAndTheClientCredentialsGrant()
    {
        var handler = StubHttpMessageHandler.ReturningToken("token-one");
        var provider = CreateProvider(handler, out _);

        var token = await provider.GetTokenAsync(CancellationToken.None);

        Assert.Equal("token-one", token);

        var request = handler.SingleRequest;
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://us.battle.net/oauth/token", request.AbsoluteUri);
        Assert.Equal("grant_type=client_credentials", request.Body);

        // Basic, carrying the credentials in the header — they never reach a URL or a body.
        Assert.Equal("Basic", request.AuthorizationScheme);
        Assert.Equal(
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ClientId}:{Secret}")),
            request.AuthorizationParameter);
    }

    [Fact]
    public async Task GetToken_UsesTheRegionalHost_NotTheNonRegionalOAuthHost()
    {
        // external.md rules out oauth.battle.net explicitly: it resolves and looks like a sensible
        // default, but it has a documented history of intermittent 403s. This is the assertion that stops
        // someone "simplifying" the host table back to it.
        var handler = StubHttpMessageHandler.ReturningToken("token-one");
        var provider = CreateProvider(handler, out _);

        await provider.GetTokenAsync(CancellationToken.None);

        Assert.DoesNotContain("oauth.battle.net", handler.SingleRequest.AbsoluteUri);
        Assert.Contains("us.battle.net", handler.SingleRequest.AbsoluteUri);
    }

    [Theory]
    [InlineData("eu", "https://eu.battle.net/oauth/token")]
    [InlineData("kr", "https://kr.battle.net/oauth/token")]
    [InlineData("sea", "https://sea.battle.net/oauth/token")]
    public async Task GetToken_MintsAgainstTheConfiguredRegion(string region, string expectedUri)
    {
        var handler = StubHttpMessageHandler.ReturningToken("token-one");
        var provider = CreateProvider(handler, out _, options => options.Region = region);

        await provider.GetTokenAsync(CancellationToken.None);

        Assert.Equal(expectedUri, handler.SingleRequest.AbsoluteUri);
    }

    [Fact]
    public async Task GetToken_CalledTwiceWithinTheTokenLifetime_MintsOnlyOnce()
    {
        var handler = StubHttpMessageHandler.ReturningToken("token-one");
        var provider = CreateProvider(handler, out _);

        var first = await provider.GetTokenAsync(CancellationToken.None);
        var second = await provider.GetTokenAsync(CancellationToken.None);

        Assert.Equal("token-one", first);
        Assert.Equal("token-one", second);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task GetToken_FiftyConcurrentCallersOnAColdCache_MintExactlyOneToken()
    {
        // The "refresh once, under a lock, not once per in-flight request" assertion. The gate makes every
        // caller pile up on a mint that has not completed, which is the situation the double-check inside
        // the lock exists for — without it this is fifty token requests.
        var release = new TaskCompletionSource();
        var handler = new StubHttpMessageHandler(async (_, _) =>
        {
            await release.Task;
            return StubHttpMessageHandler.RespondWithToken("token-one");
        });

        var provider = CreateProvider(handler, out _);

        var callers = Enumerable.Range(0, 50)
            .Select(_ => provider.GetTokenAsync(CancellationToken.None))
            .ToArray();

        release.SetResult();
        var tokens = await Task.WhenAll(callers);

        Assert.Equal(1, handler.RequestCount);
        Assert.All(tokens, token => Assert.Equal("token-one", token));
    }

    [Fact]
    public async Task GetToken_AfterTheTokenPassesItsRefreshPoint_MintsAgain()
    {
        var handler = StubHttpMessageHandler.Scripted((_, attempt) =>
            StubHttpMessageHandler.RespondWithToken($"token-{attempt}", expiresInSeconds: 3600));

        var provider = CreateProvider(handler, out var time);

        Assert.Equal("token-1", await provider.GetTokenAsync(CancellationToken.None));

        // Still inside the window: an hour's lifetime less the five-minute skew.
        time.Advance(TimeSpan.FromMinutes(50));
        Assert.Equal("token-1", await provider.GetTokenAsync(CancellationToken.None));
        Assert.Equal(1, handler.RequestCount);

        // Past the refresh point but before Blizzard's stated expiry — which is the whole point of the
        // skew: we renew while the old token would still have worked.
        time.Advance(TimeSpan.FromMinutes(6));
        Assert.Equal("token-2", await provider.GetTokenAsync(CancellationToken.None));
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task GetToken_WhenTheLifetimeIsShorterThanTheSkew_StillCachesTheToken()
    {
        // A naive "expiry minus skew" would put the refresh point in the past and mint a fresh token on
        // every single request — a self-inflicted rate-limit problem.
        var handler = StubHttpMessageHandler.Scripted((_, attempt) =>
            StubHttpMessageHandler.RespondWithToken($"token-{attempt}", expiresInSeconds: 60));

        var provider = CreateProvider(handler, out _);

        Assert.Equal("token-1", await provider.GetTokenAsync(CancellationToken.None));
        Assert.Equal("token-1", await provider.GetTokenAsync(CancellationToken.None));
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task GetToken_WithNoCredentialsConfigured_ReturnsNullAndCallsBlizzardNotAtAll()
    {
        // external.md: "Missing credentials are a no-op, not a crash." Offline development is a
        // first-class case, so this must not throw and must not make a request it knows will fail.
        var handler = StubHttpMessageHandler.ReturningToken("unreachable");
        var provider = CreateProvider(handler, out _, options =>
        {
            options.ClientId = null;
            options.ClientSecret = null;
        });

        var token = await provider.GetTokenAsync(CancellationToken.None);

        Assert.Null(token);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task GetToken_WithOnlyAClientId_TreatsTheIntegrationAsUnconfigured()
    {
        // Half a credential pair is not a usable credential, and trying it would spend a request to learn
        // what we already know.
        var handler = StubHttpMessageHandler.ReturningToken("unreachable");
        var provider = CreateProvider(handler, out _, options => options.ClientSecret = "   ");

        Assert.Null(await provider.GetTokenAsync(CancellationToken.None));
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task GetToken_WhenBlizzardRejectsTheCredentials_ReturnsNullWithoutThrowing()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.Unauthorized);
        var provider = CreateProvider(handler, out _);

        Assert.Null(await provider.GetTokenAsync(CancellationToken.None));
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task GetToken_WhenBlizzardIsUnreachable_ReturnsNullWithoutThrowing()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            throw new HttpRequestException("no route to host"));

        var provider = CreateProvider(handler, out _);

        Assert.Null(await provider.GetTokenAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetToken_WhenTheResponseCarriesNoToken_ReturnsNullWithoutThrowing()
    {
        var handler = new StubHttpMessageHandler(_ =>
            StubHttpMessageHandler.Respond(HttpStatusCode.OK, """{"token_type":"bearer","expires_in":3600}"""));

        var provider = CreateProvider(handler, out _);

        Assert.Null(await provider.GetTokenAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetToken_AfterAFailedMint_DoesNotRetryUntilTheCooldownElapses()
    {
        // A battle.net outage must not become a tight loop against their token endpoint. Every cache-first
        // read asks for a token, so without the cooldown an outage turns into one outbound request per
        // inbound request.
        var handler = StubHttpMessageHandler.Scripted((_, attempt) => attempt <= 1
            ? StubHttpMessageHandler.Respond(HttpStatusCode.ServiceUnavailable)
            : StubHttpMessageHandler.RespondWithToken("token-recovered"));

        var provider = CreateProvider(handler, out var time);

        Assert.Null(await provider.GetTokenAsync(CancellationToken.None));
        Assert.Equal(1, handler.RequestCount);

        // Inside the cooldown: no second attempt.
        Assert.Null(await provider.GetTokenAsync(CancellationToken.None));
        Assert.Equal(1, handler.RequestCount);

        // Past it: we try again, and Blizzard has recovered.
        time.Advance(TimeSpan.FromSeconds(31));
        Assert.Equal("token-recovered", await provider.GetTokenAsync(CancellationToken.None));
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task GetToken_WhenARefreshFails_KeepsServingTheTokenItAlreadyHas()
    {
        // A failed refresh must never evict a token that still works. Anything held is at most the skew
        // away from its stated expiry, so it is very likely still valid at Blizzard — and a nearly-expired
        // token beats no token.
        var handler = StubHttpMessageHandler.Scripted((_, attempt) => attempt <= 1
            ? StubHttpMessageHandler.RespondWithToken("token-one", expiresInSeconds: 3600)
            : StubHttpMessageHandler.Respond(HttpStatusCode.InternalServerError));

        var provider = CreateProvider(handler, out var time);

        Assert.Equal("token-one", await provider.GetTokenAsync(CancellationToken.None));

        // Past the refresh point, so the next call tries to renew — and the renewal fails.
        time.Advance(TimeSpan.FromMinutes(56));

        Assert.Equal("token-one", await provider.GetTokenAsync(CancellationToken.None));
        Assert.Equal(2, handler.RequestCount);
    }

    private static BlizzardTokenProvider CreateProvider(
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

        // The provider resolves its client by name, exactly as it does in the app — so this also pins that
        // it asks for the dedicated OAuth client rather than the gateway's typed one, which carries
        // BlizzardAuthHandler and would recurse.
        var httpClient = new HttpClient(handler) { BaseAddress = options.ResolveOAuthBaseAddress() };
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(BlizzardDefaults.OAuthHttpClientName).Returns(httpClient);

        return new BlizzardTokenProvider(
            factory,
            new OptionsWrapper<BlizzardOptions>(options),
            timeProvider,
            NullLogger<BlizzardTokenProvider>.Instance);
    }
}
