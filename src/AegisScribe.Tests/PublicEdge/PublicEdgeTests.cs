using System.Net;
using System.Net.Http.Json;
using AegisScribe.Tests.Auth;

namespace AegisScribe.Tests.PublicEdge;

[Collection("AegisScribe API - Public Edge")]
public class PublicEdgeTests(AegisScribeAppFixture fixture) : IDisposable
{
    // fixture.ApiClient carries ServiceDefaults' standard resilience handler, which retries
    // transient-looking responses (429 included) with backoff — great for production, but it means
    // a client deliberately trying to observe a 429 (or anything sent after one has already been
    // triggered in this collection) gets stuck retrying past the resilience handler's own 30s total
    // timeout instead of ever seeing the response (confirmed against dotnet/aspire#3431, which
    // documents that CreateHttpClient's resilience settings aren't overridable per-call). These
    // tests build their own plain client, pointed at the same resolved endpoint, with no retries.
    private readonly HttpClient _rawClient = new() { BaseAddress = fixture.ApiClient.BaseAddress };

    private static string UniqueEmail() => $"edge-{Guid.NewGuid():N}@example.com";

    [Fact]
    public async Task CrossOriginRequest_CarriesNoCorsHeaders()
    {
        // An HttpClient doesn't enforce CORS itself — a browser does. The only server-observable
        // proof that "no CORS policy" holds is the absence of Access-Control-* response headers,
        // which is exactly what stops a browser reading the response cross-origin even with a
        // stolen token (backend.md -> "The API's public edge").
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
