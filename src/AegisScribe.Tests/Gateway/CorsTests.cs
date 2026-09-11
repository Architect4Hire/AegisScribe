using AegisScribe.Tests.Auth;

namespace AegisScribe.Tests.Gateway;

// Preflight OPTIONS requests are handled entirely by the CORS middleware inside the gateway and
// never reach the proxy or the API, so these add no load to this collection's anonymous
// rate-limit budget the way a full login flow would (1B.7).
[Collection("AegisScribe API - Gateway")]
public class CorsTests(AegisScribeAppFixture fixture)
{
    private static HttpRequestMessage PreflightRequest(string origin) =>
        new(HttpMethod.Options, "/api/v1/auth/whoami")
        {
            Headers =
            {
                { "Origin", origin },
                { "Access-Control-Request-Method", "GET" },
            },
        };

    // THE test BEHAVIOR asks for: a preflight from an origin that isn't the configured SPA origin
    // must be rejected — gateway.md -> "CORS": reflecting the caller's origin is the same as having
    // no CORS at all, so the only acceptable "rejected" signal is the complete absence of
    // Access-Control-Allow-Origin, not an origin-specific value.
    [Fact]
    public async Task Preflight_FromUnlistedOrigin_IsRejected()
    {
        using var http = new HttpClient { BaseAddress = fixture.GatewayBaseAddress };
        var response = await http.SendAsync(PreflightRequest("https://evil.example.com"));

        Assert.False(
            response.Headers.Contains("Access-Control-Allow-Origin"),
            "An unlisted origin must not receive an Access-Control-Allow-Origin header.");
    }

    // Control: proves the assertion above isn't vacuously true because CORS is misconfigured or
    // missing entirely.
    [Fact]
    public async Task Preflight_FromAllowedOrigin_Succeeds()
    {
        using var http = new HttpClient { BaseAddress = fixture.GatewayBaseAddress };
        var origin = fixture.SpaOrigin.GetLeftPart(UriPartial.Authority);
        var response = await http.SendAsync(PreflightRequest(origin));

        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var allowOrigin));
        Assert.Equal(origin, Assert.Single(allowOrigin!));

        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Credentials", out var allowCredentials));
        Assert.Equal("true", Assert.Single(allowCredentials!));
    }
}
