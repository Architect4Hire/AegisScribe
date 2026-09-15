using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Tests.Auth;
using AegisScribe.Tests.Gateway;

namespace AegisScribe.Tests.Tenancy;

// Its own collection/AppHost instance, the same reason HeaderSanitisationCollection exists: each
// test here registers a user and drives a full code+PKCE exchange, which combined with the "AegisScribe
// API" collection's own registrations/token mints trips the API's 20/min anonymous-IP rate-limit
// bucket (backend.md -> "The API's public edge") when the full suite runs.

[Collection("AegisScribe API")]
public class TenantResolutionTests(AegisScribeAppFixture fixture)
{
    private static async Task<string> RegisterAndGetTokenAsync(AegisScribeAppFixture fixture)
    {
        var email = await GatewayLoginFlow.RegisterUserAsync(fixture);
        var (accessToken, _) = await DirectOAuthFlow.LoginAsBffClientAsync(fixture, email);
        return accessToken;
    }

    private static Task<HttpResponseMessage> GetAsync(AegisScribeAppFixture fixture, string path, string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return fixture.ApiClient.SendAsync(request);
    }

    [Fact]
    public async Task ValidMember_ReachesTheEndpointWithTheResolvedTenant()
    {
        var (email, userId) = await GatewayLoginFlow.RegisterUserWithIdAsync(fixture);
        var (accessToken, _) = await DirectOAuthFlow.LoginAsBffClientAsync(fixture, email);

        var tenant = await TenantSeeding.CreateTenantAsync(fixture);
        // GET /t/{slug} is Owner-only (2.7) — a lower rank is covered by MemberButNotOwner_GetsForbidden.
        await TenantSeeding.AddMembershipAsync(fixture, tenant.Id, userId, TenantRole.Owner);

        var response = await GetAsync(fixture, $"/api/v1/t/{tenant.Slug}", accessToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal(tenant.Id, body!.RootElement.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task MemberButNotOwner_GetsForbidden()
    {
        // A real member confirms the tenant exists, so a rank shortfall is 403, not 404 — only a
        // non-member (below) gets 404, per tenancy.md's "403 confirms the tenant exists" rule.
        var (email, userId) = await GatewayLoginFlow.RegisterUserWithIdAsync(fixture);
        var (accessToken, _) = await DirectOAuthFlow.LoginAsBffClientAsync(fixture, email);

        var tenant = await TenantSeeding.CreateTenantAsync(fixture);
        await TenantSeeding.AddMembershipAsync(fixture, tenant.Id, userId, TenantRole.Officer);

        var response = await GetAsync(fixture, $"/api/v1/t/{tenant.Slug}", accessToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task NonMember_GetsNotFound()
    {
        var (memberEmail, memberId) = await GatewayLoginFlow.RegisterUserWithIdAsync(fixture);
        var tenant = await TenantSeeding.CreateTenantAsync(fixture);
        await TenantSeeding.AddMembershipAsync(fixture, tenant.Id, memberId);

        var outsiderToken = await RegisterAndGetTokenAsync(fixture);

        var response = await GetAsync(fixture, $"/api/v1/t/{tenant.Slug}", outsiderToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UnknownSlug_GetsNotFound_IndistinguishableFromNonMember()
    {
        var accessToken = await RegisterAndGetTokenAsync(fixture);

        var response = await GetAsync(fixture, $"/api/v1/t/{TenantSeeding.UniqueSlug()}", accessToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TenantLessRoute_IsUnaffectedByTheMiddleware()
    {
        // /api/v1/auth/whoami: the simplest tenant-less route that needs an authenticated caller and
        // touches no data, so the only thing under test is that the middleware leaves it alone.
        // (/me is covered on its own in MeEndpointTests.)
        var accessToken = await RegisterAndGetTokenAsync(fixture);

        var response = await GetAsync(fixture, "/api/v1/auth/whoami", accessToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
