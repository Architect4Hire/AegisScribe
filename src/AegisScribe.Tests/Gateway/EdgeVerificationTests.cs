using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AegisScribe.Tests.Auth;

namespace AegisScribe.Tests.Gateway;

// The auth/gateway boundary, end to end. The strip test and the unlisted-CORS-origin test have their
// own files — HeaderSanitisationTests and CorsTests — and are not repeated here.
[Collection("AegisScribe API")]
public class EdgeVerificationTests(AegisScribeAppFixture fixture)
{
    // No gateway response leaks a JWT in its body, headers, or session cookie. The session cookie is a
    // Data-Protection-encrypted blob rather than a JWT, but this proves that instead of assuming it
    // (gateway.md: "grep for eyJ").
    [Fact]
    public async Task NoGatewayResponse_LeaksAJwt()
    {
        const string jwtPrefix = "eyJ";

        var email = await GatewayLoginFlow.RegisterUserAsync(fixture);
        using var client = await GatewayLoginFlow.LoginAsync(fixture, email);

        Assert.DoesNotContain(jwtPrefix, client.SetCookieHeader, StringComparison.Ordinal);

        var whoami = await client.SendAuthenticatedAsync(HttpMethod.Get, "/api/v1/auth/whoami");
        await AssertResponseCarriesNoJwtAsync(whoami, jwtPrefix);

        var logout = await client.SendAuthenticatedAsync(HttpMethod.Post, "/auth/logout");
        await AssertResponseCarriesNoJwtAsync(logout, jwtPrefix);
    }

    private static async Task AssertResponseCarriesNoJwtAsync(HttpResponseMessage response, string jwtPrefix)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(jwtPrefix, body, StringComparison.Ordinal);

        foreach (var header in response.Headers.Concat(response.Content.Headers))
        {
            foreach (var value in header.Value)
            {
                Assert.DoesNotContain(jwtPrefix, value, StringComparison.Ordinal);
            }
        }
    }

    // The anonymous case, with no session at all. No character-lookup endpoint exists yet, so
    // registration stands in as the representative anonymous route.
    [Fact]
    public async Task AnonymousRoute_SucceedsWithNoSession()
    {
        var registration = new Dictionary<string, string>
        {
            ["email"] = GatewayLoginFlow.UniqueEmail(),
            ["password"] = GatewayLoginFlow.Password,
            ["displayName"] = "Anonymous Route Test",
        };

        using var http = new HttpClient { BaseAddress = fixture.GatewayBaseAddress };
        var response = await http.PostAsJsonAsync("/api/v1/auth/register", registration);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // Logout_EndsSessionImmediately proves the "next request is anonymous" half; this proves the
    // revocation mechanism logout depends on. The gateway's session never exposes its refresh token to
    // a caller, so this drives the identical connect/revoke + connect/token calls directly, as the same
    // aegisscribe-bff client.
    [Fact]
    public async Task Logout_RevokesRefreshToken_ReplayFails()
    {
        var email = await GatewayLoginFlow.RegisterUserAsync(fixture);
        var (_, firstRefreshToken) = await DirectOAuthFlow.LoginAsBffClientAsync(fixture, email);

        // Self-contained positive control: redeem once and require 200, proving this token chain is
        // genuinely live before revoking it. Rolling refresh tokens (auth.md) mean this redemption
        // itself rotates the token, so the one revoked and replayed below is the new one.
        using var redeemed = await RedeemRefreshTokenAsync(firstRefreshToken);
        Assert.Equal(HttpStatusCode.OK, redeemed.StatusCode);
        var refreshToken = (await redeemed.Content.ReadFromJsonAsync<JsonDocument>())!
            .RootElement.GetProperty("refresh_token").GetString()!;

        var revoke = await fixture.ApiClient.PostAsync("connect/revoke", new FormUrlEncodedContent(
        [
            new("token", refreshToken),
            new("token_type_hint", "refresh_token"),
            new("client_id", "aegisscribe-bff"),
            new("client_secret", fixture.BffSecret),
        ]));
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);

        using var replay = await RedeemRefreshTokenAsync(refreshToken);
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
    }

    private Task<HttpResponseMessage> RedeemRefreshTokenAsync(string refreshToken) =>
        fixture.ApiClient.PostAsync("connect/token", new FormUrlEncodedContent(
        [
            new("grant_type", "refresh_token"),
            new("refresh_token", refreshToken),
            new("client_id", "aegisscribe-bff"),
            new("client_secret", fixture.BffSecret),
        ]));

    // A request spanning token expiry succeeds with no browser round trip. RefreshCoordinator's
    // 30-second buffer means a 1-second access token already reads as expired on this next call, so no
    // delay is needed. A single clean 200 IS "no browser round trip": no redirect, no 401-then-retry
    // visible to the caller.
    [Fact]
    public async Task RequestSpanningTokenExpiry_SucceedsTransparently()
    {
        var email = await GatewayLoginFlow.RegisterUserAsync(fixture);
        using var client = await GatewayLoginFlow.LoginAsync(fixture, email, tokenLifetimeSeconds: 1);

        var response = await client.SendAuthenticatedAsync(HttpMethod.Get, "/api/v1/auth/whoami");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // aegisscribe-mobile is a public client with no secret, so PKCE's verifier is the only thing
    // binding an authorization code to the app that requested it (auth.md). PKCE is required globally
    // at the protocol layer, so what this proves is that the verifier is actually checked at
    // redemption, not merely declared upfront.
    [Fact]
    public async Task MobileTokenRequest_WithoutPkceVerifier_IsRejected()
    {
        var email = await GatewayLoginFlow.RegisterUserAsync(fixture);
        var (_, challenge) = DirectOAuthFlow.GeneratePkcePair();
        var code = await DirectOAuthFlow.AuthorizeAsync(
            fixture, "aegisscribe-mobile", "aegisscribe://auth/callback",
            email, GatewayLoginFlow.Password, challenge);

        var response = await fixture.ApiClient.PostAsync("connect/token", new FormUrlEncodedContent(
        [
            new("grant_type", "authorization_code"),
            new("code", code),
            new("redirect_uri", "aegisscribe://auth/callback"),
            new("client_id", "aegisscribe-mobile"),
            // code_verifier deliberately omitted — aegisscribe-mobile has no secret either, so the
            // verifier is the only thing that could still bind this code to the caller.
        ]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // Positive control for the test above: the identical flow with the matching verifier must succeed,
    // so a 400 there is evidence the omitted verifier specifically is what's rejected — not an
    // unrelated regression that would make either test pass or fail for the wrong reason.
    [Fact]
    public async Task MobileTokenRequest_WithMatchingPkceVerifier_Succeeds()
    {
        var email = await GatewayLoginFlow.RegisterUserAsync(fixture);
        var (verifier, challenge) = DirectOAuthFlow.GeneratePkcePair();
        var code = await DirectOAuthFlow.AuthorizeAsync(
            fixture, "aegisscribe-mobile", "aegisscribe://auth/callback",
            email, GatewayLoginFlow.Password, challenge);

        var response = await fixture.ApiClient.PostAsync("connect/token", new FormUrlEncodedContent(
        [
            new("grant_type", "authorization_code"),
            new("code", code),
            new("redirect_uri", "aegisscribe://auth/callback"),
            new("client_id", "aegisscribe-mobile"),
            new("code_verifier", verifier),
        ]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
