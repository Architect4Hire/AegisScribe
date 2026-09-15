using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Tests.Gateway;

namespace AegisScribe.Tests.Auth;

// Its own collection/AppHost for the same reason TenantResolutionCollection has one: a full
// register + code+PKCE exchange per test would push the shared "AegisScribe API" collection past the
// API's 20/min anonymous-IP rate-limit bucket.

// Through the endpoint, the whole stack: MeController → IMeFacade → MeBusiness (via ICurrentUser)
// → IUserDataLayer → UserRepository → SQL, plus the global handler's 401 and 400 mappings.
[Collection("AegisScribe API")]
public class MeEndpointTests(AegisScribeAppFixture fixture)
{
    [Fact]
    public async Task SignedInUser_GetsTheirOwnRecord()
    {
        var (email, userId) = await GatewayLoginFlow.RegisterUserWithIdAsync(fixture);
        var (accessToken, _) = await DirectOAuthFlow.LoginAsBffClientAsync(fixture, email);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await fixture.ApiClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = await response.Content.ReadFromJsonAsync<UserServiceModel>();
        Assert.Equal(userId, user!.Id);
        Assert.Equal(email, user.Email);
    }

    [Fact]
    public async Task SignedInUser_SeesTheirTenantMembershipsWithRoles()
    {
        var (email, userId) = await GatewayLoginFlow.RegisterUserWithIdAsync(fixture);
        var (accessToken, _) = await DirectOAuthFlow.LoginAsBffClientAsync(fixture, email);

        var tenant = await Tenancy.TenantSeeding.CreateTenantAsync(fixture);
        await Tenancy.TenantSeeding.AddMembershipAsync(fixture, tenant.Id, userId, TenantRole.Officer);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await fixture.ApiClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Enums cross the wire as strings (api-contract.md); the client needs the matching converter,
        // just as a real client would configure once rather than per response type.
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
        var user = await response.Content.ReadFromJsonAsync<UserServiceModel>(jsonOptions);
        var membership = Assert.Single(user!.Memberships, m => m.TenantId == tenant.Id);
        Assert.Equal(tenant.Slug, membership.TenantSlug);
        Assert.Equal(TenantRole.Officer, membership.Role);
    }

    [Fact]
    public async Task ClientCredentialsToken_IsNotAUser_NoMeAndNoTenant()
    {
        var accessToken = await MintOpsTokenAsync();

        // /me: a valid token that names no Identity user — the domain throws
        // AuthenticationRequiredException, and the global handler answers exactly as [Authorize]
        // would: 401, no body.
        var me = await GetAsync("/api/v1/me", accessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        Assert.Equal(0, (await me.Content.ReadAsByteArrayAsync()).Length);

        // A tenant that really exists: a machine token must not resolve into it. Its sub is a client
        // id, never a user, so it is a member of nothing — the same 404 as an unknown slug.
        var tenant = await Tenancy.TenantSeeding.CreateTenantAsync(fixture);
        var tenantRoute = await GetAsync($"/api/v1/t/{tenant.Slug}", accessToken);
        Assert.Equal(HttpStatusCode.NotFound, tenantRoute.StatusCode);
    }

    private async Task<string> MintOpsTokenAsync()
    {
        var tokenResponse = await fixture.ApiClient.PostAsync("connect/token", new FormUrlEncodedContent(
        [
            new("grant_type", "client_credentials"),
            new("client_id", "aegisscribe-ops"),
            new("client_secret", fixture.OpsClientSecret),
        ]));
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        using var tokenBody = await tokenResponse.Content.ReadFromJsonAsync<JsonDocument>();
        return tokenBody!.RootElement.GetProperty("access_token").GetString()!;
    }

    private Task<HttpResponseMessage> GetAsync(string path, string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return fixture.ApiClient.SendAsync(request);
    }

    [Fact]
    public async Task Register_FailingValidation_IsAProblemDetailsWithPerFieldErrors()
    {
        var response = await fixture.ApiClient.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = "not-an-email",
            password = "",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        var errors = body!.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("Email", out _));
        Assert.True(errors.TryGetProperty("Password", out _));
    }
}
