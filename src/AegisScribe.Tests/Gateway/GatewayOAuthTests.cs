using System.Net;
using AegisScribe.Tests.Auth;

namespace AegisScribe.Tests.Gateway;

[Collection("AegisScribe API")]
public class GatewayOAuthTests(AegisScribeAppFixture fixture)
{
    [Fact]
    public async Task CookieAttributes_MatchRestriction()
    {
        var email = await GatewayLoginFlow.RegisterUserAsync(fixture);
        using var client = await GatewayLoginFlow.LoginAsync(fixture, email);

        var setCookie = client.SetCookieHeader;

        Assert.Contains("HttpOnly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SameSite=Lax", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Domain=.aegisscribe.com", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("__Secure-", setCookie);
    }

    [Fact]
    public async Task SingleFlightRefresh_OneRefreshUnderBurst()
    {
        // A 1-second lifetime means the very first proxied call after login already sees an
        // expired access token (RefreshCoordinator's 30s buffer easily exceeds it) — no need to
        // wait out a real expiry.
        var email = await GatewayLoginFlow.RegisterUserAsync(fixture);
        using var client = await GatewayLoginFlow.LoginAsync(fixture, email, tokenLifetimeSeconds: 1);

        var burst = Enumerable.Range(0, 10).Select(_ => client.SendAuthenticatedAsync(HttpMethod.Get, "/api/v1/auth/whoami"));
        var responses = await Task.WhenAll(burst);

        // If single-flight failed, a losing racer's redeem attempt hits an already-consumed
        // refresh token, RefreshCoordinator signs it out, and that proxied call goes through with
        // no Authorization header — surfacing here as a spurious 401 instead of 200.
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact]
    public async Task Logout_EndsSessionImmediately()
    {
        var email = await GatewayLoginFlow.RegisterUserAsync(fixture);
        using var client = await GatewayLoginFlow.LoginAsync(fixture, email);

        var beforeLogout = await client.SendAuthenticatedAsync(HttpMethod.Get, "/api/v1/auth/whoami");
        Assert.Equal(HttpStatusCode.OK, beforeLogout.StatusCode);

        var logoutResponse = await client.SendAuthenticatedAsync(HttpMethod.Post, "/auth/logout");
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        // Instant, per gateway.md — the ticket is gone from Redis, not merely expired. The
        // refresh-token revocation half of this same logout call gets its dedicated replay-based
        // proof in 1B.9's edge verification, alongside the rest of the seven-point suite.
        var afterLogout = await client.SendAuthenticatedAsync(HttpMethod.Get, "/api/v1/auth/whoami");
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
    }
}
