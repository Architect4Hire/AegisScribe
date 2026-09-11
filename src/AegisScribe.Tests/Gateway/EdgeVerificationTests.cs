using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AegisScribe.Tests.Auth;

namespace AegisScribe.Tests.Gateway;

// 1B.9: proves the auth/gateway boundary end to end. Items 1 (the strip test) and 3 (unlisted CORS
// origin rejected) already have dedicated tests — HeaderSanitisationTests.ForgedAuthorizationHeader_IsStripped
// and CorsTests.Preflight_FromUnlistedOrigin_IsRejected — and aren't repeated here.
[Collection("AegisScribe API - Edge Verification")]
public class EdgeVerificationTests(AegisScribeAppFixture fixture)
{
    // Item 2: no gateway response leaks a JWT — the base64 prefix every JWT starts with — in its
    // body, its headers, or the session cookie. The session cookie is a Data-Protection-encrypted
    // blob, not a JWT, but this proves that instead of assuming it (gateway.md's own testing
    // checklist: "grep for eyJ").
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

    // Item 4: the anonymous case still works, with no session at all — no character-lookup endpoint
    // exists yet this phase (armory features are later work), so registration stands in as the
    // representative anonymous route (no [Authorize] on AuthController.Register). Combined with the
    // strip test (stripping is unconditional) and
    // HeaderSanitisationTests.ForgedAuthorizationHeader_WithNoSession_DoesNotReachApi (a forged
    // header on a *protected* route is discarded, not forwarded), this fully covers "anonymous still
    // works, and nothing invented is ever forwarded."
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

    // Item 5: logout revokes the refresh token, so a replay of it fails. Logout_EndsSessionImmediately
    // already proves the "next request is anonymous immediately" half; this proves the other half —
    // the actual revocation mechanism logout depends on. The gateway's own session never exposes its
    // refresh token to a caller (that's the point of the BFF pattern), so this drives the identical
    // connect/revoke + connect/token calls directly, as the same aegisscribe-bff client with its own
    // real secret — the exact call shape Gateway/Program.cs's /auth/logout handler already makes.
    [Fact]
    public async Task Logout_RevokesRefreshToken_ReplayFails()
    {
        var email = await GatewayLoginFlow.RegisterUserAsync(fixture);
        var (_, firstRefreshToken) = await DirectOAuthFlow.LoginAsBffClientAsync(fixture, email);

        // Self-contained positive control: redeem once first and require 200, proving this specific
        // token chain is genuinely live before revoking it — rather than leaning on some other test
        // elsewhere in the suite to have already shown a normal (non-revoked) redemption succeeds.
        // Rolling refresh tokens (auth.md) means this redemption itself rotates the token, so the
        // one actually revoked and replayed below is the new one this call returns, not the original.
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

    // Item 6: a request spanning token expiry succeeds with no browser round trip. RefreshCoordinator's
    // 30-second buffer means a 1-second access token already reads as expired on this very next call —
    // no Task.Delay needed. A single clean 200 IS "no browser round trip": no redirect, no
    // 401-then-retry visible to the caller, just a transparent server-side refresh.
    [Fact]
    public async Task RequestSpanningTokenExpiry_SucceedsTransparently()
    {
        var email = await GatewayLoginFlow.RegisterUserAsync(fixture);
        using var client = await GatewayLoginFlow.LoginAsync(fixture, email, tokenLifetimeSeconds: 1);

        var response = await client.SendAuthenticatedAsync(HttpMethod.Get, "/api/v1/auth/whoami");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // Item 7: aegisscribe-mobile is a public client with no secret — PKCE's verifier is the only
    // thing binding an authorization code to the app that requested it (auth.md: "a public client
    // with no PKCE is a Blocker"). PKCE is required globally at the protocol layer
    // (RequireProofKeyForCodeExchange() in ApiService/Program.cs), so the code_challenge at authorize
    // time is mandatory regardless of client — what this proves is that the verifier is actually
    // checked at redemption, not merely declared upfront.
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

    // Positive control for the test above: the identical mobile-client flow with the matching
    // verifier must succeed, so a 400 there is evidence the omitted verifier specifically is what's
    // rejected — not an unrelated regression (redirect_uri handling for the Native application type,
    // a missing scope permission, etc.) that would make either test pass or fail for the wrong reason.
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
