using System.Net;
using System.Net.Http.Json;
using AegisScribe.Tests.Auth;

namespace AegisScribe.Tests.PublicEdge;

[Collection("AegisScribe API - Public Edge")]
public class PublicEdgeTests(TightRateLimitAppFixture fixture) : IDisposable
{
    // fixture.ApiClient carries the standard resilience handler, which retries 429s with backoff — so a
    // client deliberately trying to OBSERVE a 429 gets stuck retrying past the handler's own timeout
    // instead of ever seeing the response, and those settings are not overridable per-call
    // (dotnet/aspire#3431). These tests use their own plain client with no retries.
    private readonly HttpClient _rawClient = new() { BaseAddress = fixture.ApiClient.BaseAddress };

    private static string UniqueEmail() => $"edge-{Guid.NewGuid():N}@example.com";

    [Fact]
    public async Task CrossOriginRequest_CarriesNoCorsHeaders()
    {
        // An HttpClient does not enforce CORS — a browser does. The only server-observable proof that
        // "no CORS policy" holds is the absence of Access-Control-* headers, which is what stops a
        // browser reading the response cross-origin even with a stolen token (backend.md).
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/register")
        {
            Content = JsonContent.Create(new { email = UniqueEmail(), password = "Sup3r$ecretPwd!" }),
        };
        request.Headers.Add("Origin", "https://evil.example.com");

        var response = await _rawClient.SendAsync(request);

        Assert.DoesNotContain(response.Headers, h => h.Key.StartsWith("Access-Control-", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExceedingTheAnonymousLimit_Returns429WithRetryAfter()
    {
        // The anonymous IP bucket is the tightest (PermitLimit = 20 per minute in Program.cs) —
        // firing one more than that from the same client should trip it.
        HttpResponseMessage? last = null;
        for (var i = 0; i < 21; i++)
        {
            last = await _rawClient.PostAsJsonAsync("/api/v1/auth/register", new
            {
                email = UniqueEmail(),
                password = "Sup3r$ecretPwd!",
            });
        }

        Assert.NotNull(last);
        Assert.Equal(HttpStatusCode.TooManyRequests, last!.StatusCode);
        Assert.True(last.Headers.RetryAfter is not null, "Expected a Retry-After header on the 429 response.");
    }

    public void Dispose() => _rawClient.Dispose();
}
