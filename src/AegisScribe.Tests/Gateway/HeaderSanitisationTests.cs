using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AegisScribe.Tests.Auth;

namespace AegisScribe.Tests.Gateway;

[Collection("AegisScribe API")]
public class HeaderSanitisationTests(AegisScribeAppFixture fixture)
{
    // THE strip test (gateway.md → "Header sanitisation", the RESTRICTION calls this "the single
    // most important control in the auth surface"): a request carrying a forged Authorization
    // header must still reach the API carrying the gateway's own token, not the forged one.
    [Fact]
    public async Task ForgedAuthorizationHeader_IsStripped()
    {
        var (email, id) = await GatewayLoginFlow.RegisterUserWithIdAsync(fixture);
        using var client = await GatewayLoginFlow.LoginAsync(fixture, email);

        var response = await client.SendAuthenticatedAsync(
            HttpMethod.Get, "/api/v1/auth/whoami", ("Authorization", "Bearer forged-garbage-token"));

        // If the forged header ever reached the API unstripped, OpenIddict couldn't decrypt
        // "forged-garbage-token" as a JWE and this would be a 401, not a 200 — a sharp, unmistakable
        // failure signal rather than a silent pass.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        // sub matches the logged-in user, not something a forged token could have claimed instead.
        Assert.Equal(id, body!.RootElement.GetProperty("sub").GetString());
    }

    // The gap the bug actually lived in: HttpRequestMessage.Headers.Authorization is a single-value
    // property, so when the gateway DOES have a session token, assigning to it already overwrites a
    // forwarded client header. Anonymous — no session at all — is the case where nothing would
    // otherwise touch the header, letting a forged one sail straight through untouched.
    [Fact]
    public async Task ForgedAuthorizationHeader_WithNoSession_DoesNotReachApi()
    {
        using var http = new HttpClient { BaseAddress = fixture.GatewayBaseAddress };
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/whoami");
        request.Headers.Add("Authorization", "Bearer forged-garbage-token");

        var response = await http.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // A bare "WWW-Authenticate: Bearer" (no error="invalid_token" detail) means the API saw no
        // bearer token at all — proving the forged header was discarded before it ever reached the
        // API, not merely presented and rejected as invalid.
        var challenge = response.Headers.WwwAuthenticate.ToString();
        Assert.Contains("Bearer", challenge, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("invalid_token", challenge, StringComparison.OrdinalIgnoreCase);
    }
}
