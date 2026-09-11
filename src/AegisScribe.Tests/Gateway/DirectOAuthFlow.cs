using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AegisScribe.Tests.Auth;

namespace AegisScribe.Tests.Gateway;

// Drives connect/authorize -> connect/token directly against the API (fixture.ApiClient), bypassing
// the gateway entirely. Used only where a test needs to hold a real token itself — the gateway's own
// session never exposes one to a caller by design, so there is no legitimate way to grab "the" token
// it minted for its own login flow (1B.9's edge verification). No correlation/nonce cookies needed
// here: those belong to the browser-facing OIDC handler in the gateway, not the raw protocol.
internal static class DirectOAuthFlow
{
    private static readonly Regex CodeFieldPattern =
        new("<input type=\"hidden\" name=\"code\" value=\"(?<value>[^\"]*)\"", RegexOptions.Compiled);

    public static (string Verifier, string Challenge) GeneratePkcePair()
    {
        var verifier = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return (verifier, challenge);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // All OAuth params travel as form fields, never a query string — OpenIddict parses a POST from
    // the form body only (AuthorizationController.cs's own comment on this). response_mode=form_post
    // is requested unconditionally so the response is a parseable HTML form regardless of the
    // client's redirect scheme — load-bearing for aegisscribe-mobile, whose registered redirect
    // (a custom aegisscribe:// scheme) HttpClient could never follow as a real redirect.
    public static async Task<string> AuthorizeAsync(
        AegisScribeAppFixture fixture, string clientId, string redirectUri,
        string email, string password, string codeChallenge, string scope = "openid offline_access")
    {
        var formFields = new List<KeyValuePair<string, string>>
        {
            new("client_id", clientId),
            new("redirect_uri", redirectUri),
            new("response_type", "code"),
            new("scope", scope),
            new("code_challenge", codeChallenge),
            new("code_challenge_method", "S256"),
            new("response_mode", "form_post"),
            new("identifier", email),
            new("credential", password),
        };

        var response = await fixture.ApiClient.PostAsync("connect/authorize", new FormUrlEncodedContent(formFields));
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"connect/authorize sign-in failed: {response.StatusCode}");
        }

        var html = await response.Content.ReadAsStringAsync();
        var match = CodeFieldPattern.Match(html);
        if (!match.Success)
        {
            throw new InvalidOperationException("connect/authorize response carried no code.");
        }

        return WebUtility.HtmlDecode(match.Groups["value"].Value);
    }

    // Full code+PKCE exchange as aegisscribe-bff, against its actually registered redirect_uri (the
    // gateway's own /auth/callback — never navigated to here, it only has to match what's seeded).
    public static async Task<(string AccessToken, string RefreshToken)> LoginAsBffClientAsync(
        AegisScribeAppFixture fixture, string email)
    {
        var (verifier, challenge) = GeneratePkcePair();
        var redirectUri = new Uri(fixture.GatewayBaseAddress, "/auth/callback").ToString();
        var code = await AuthorizeAsync(
            fixture, "aegisscribe-bff", redirectUri, email, GatewayLoginFlow.Password, challenge);

        var tokenResponse = await fixture.ApiClient.PostAsync("connect/token", new FormUrlEncodedContent(
        [
            new("grant_type", "authorization_code"),
            new("code", code),
            new("redirect_uri", redirectUri),
            new("client_id", "aegisscribe-bff"),
            new("client_secret", fixture.BffSecret),
            new("code_verifier", verifier),
        ]));
        if (tokenResponse.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"connect/token (authorization_code) failed: {tokenResponse.StatusCode}");
        }

        using var body = await tokenResponse.Content.ReadFromJsonAsync<JsonDocument>();
        var accessToken = body!.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Token response carried no access_token.");
        var refreshToken = body.RootElement.GetProperty("refresh_token").GetString()
            ?? throw new InvalidOperationException("Token response carried no refresh_token.");
        return (accessToken, refreshToken);
    }
}
