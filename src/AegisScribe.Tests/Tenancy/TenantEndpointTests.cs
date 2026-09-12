using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Tests.Auth;
using AegisScribe.Tests.Gateway;

namespace AegisScribe.Tests.Tenancy;

// Its own collection/AppHost, the same reason TenantResolutionCollection/MeEndpointCollection have
// one: each test registers a user and drives a full code+PKCE exchange.
[CollectionDefinition("AegisScribe API - Tenant CRUD")]
public class TenantEndpointCollection : ICollectionFixture<AegisScribeAppFixture>;

// Through the endpoint: TenantsController/TenantController -> ITenantFacade -> ITenantBusiness ->
// ITenantDataLayer -> ITenantRepository -> SQL, plus the idempotency filter and the TenantOwner policy.
[Collection("AegisScribe API - Tenant CRUD")]
public class TenantEndpointTests(AegisScribeAppFixture fixture)
{
    private async Task<string> RegisterAndGetTokenAsync()
    {
        var email = await GatewayLoginFlow.RegisterUserAsync(fixture);
        var (accessToken, _) = await DirectOAuthFlow.LoginAsBffClientAsync(fixture, email);
        return accessToken;
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string path, string accessToken, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    [Fact]
    public async Task Create_Authenticated_BecomesOwner_AndCanReadItBack()
    {
        var accessToken = await RegisterAndGetTokenAsync();
        var slug = TenantSeeding.UniqueSlug();

        var createResponse = await fixture.ApiClient.SendAsync(CreateRequest(HttpMethod.Post, "/api/v1/tenants", accessToken,
            new { Slug = slug, Name = "Emberwatch", TimeZoneId = "UTC" }));

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<TenantServiceModel>();
        Assert.Equal(slug, created!.Slug);

        // Reading it back is Owner-only — proves the creator was actually made the first Owner.
        var getResponse = await fixture.ApiClient.SendAsync(CreateRequest(HttpMethod.Get, $"/api/v1/t/{slug}", accessToken));
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var read = await getResponse.Content.ReadFromJsonAsync<TenantServiceModel>();
        Assert.Equal(created.Id, read!.Id);
    }

    [Fact]
    public async Task Create_Unauthenticated_Unauthorized()
    {
        var response = await fixture.ApiClient.PostAsJsonAsync("/api/v1/tenants",
            new { Slug = TenantSeeding.UniqueSlug(), Name = "Emberwatch", TimeZoneId = "UTC" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_DuplicateSlug_IsAValidationProblemOnTheSlugField()
    {
        var accessToken = await RegisterAndGetTokenAsync();
        var slug = TenantSeeding.UniqueSlug();
        var body = new { Slug = slug, Name = "Emberwatch", TimeZoneId = "UTC" };

        var first = await fixture.ApiClient.SendAsync(CreateRequest(HttpMethod.Post, "/api/v1/tenants", accessToken, body));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await fixture.ApiClient.SendAsync(CreateRequest(HttpMethod.Post, "/api/v1/tenants", accessToken, body));

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        using var problem = await second.Content.ReadFromJsonAsync<System.Text.Json.JsonDocument>();
        Assert.True(problem!.RootElement.GetProperty("errors").TryGetProperty("Slug", out _));
    }

    [Fact]
    public async Task Create_RepeatedIdempotencyKey_ReplaysTheFirstResponse_InsteadOfActingTwice()
    {
        var accessToken = await RegisterAndGetTokenAsync();
        var slug = TenantSeeding.UniqueSlug();
        var idempotencyKey = Guid.NewGuid().ToString();

        HttpRequestMessage BuildRequest()
        {
            var request = CreateRequest(HttpMethod.Post, "/api/v1/tenants", accessToken,
                new { Slug = slug, Name = "Emberwatch", TimeZoneId = "UTC" });
            request.Headers.Add("Idempotency-Key", idempotencyKey);
            return request;
        }

        var first = await fixture.ApiClient.SendAsync(BuildRequest());
        var second = await fixture.ApiClient.SendAsync(BuildRequest());

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var firstBody = await first.Content.ReadFromJsonAsync<TenantServiceModel>();
        var secondBody = await second.Content.ReadFromJsonAsync<TenantServiceModel>();
        Assert.Equal(firstBody!.Id, secondBody!.Id);
    }

    [Fact]
    public async Task Rename_Owner_UpdatesTheName()
    {
        var accessToken = await RegisterAndGetTokenAsync();
        var slug = TenantSeeding.UniqueSlug();
        var created = await (await fixture.ApiClient.SendAsync(CreateRequest(HttpMethod.Post, "/api/v1/tenants", accessToken,
            new { Slug = slug, Name = "Emberwatch", TimeZoneId = "UTC" }))).Content.ReadFromJsonAsync<TenantServiceModel>();

        var renameResponse = await fixture.ApiClient.SendAsync(
            CreateRequest(HttpMethod.Patch, $"/api/v1/t/{slug}", accessToken, new { Name = "Emberwatch Reborn" }));

        Assert.Equal(HttpStatusCode.OK, renameResponse.StatusCode);
        var renamed = await renameResponse.Content.ReadFromJsonAsync<TenantServiceModel>();
        Assert.Equal(created!.Id, renamed!.Id);
        Assert.Equal("Emberwatch Reborn", renamed.Name);
    }

    [Fact]
    public async Task Rename_NonOwnerMember_Forbidden()
    {
        var (memberEmail, memberId) = await GatewayLoginFlow.RegisterUserWithIdAsync(fixture);
        var (memberToken, _) = await DirectOAuthFlow.LoginAsBffClientAsync(fixture, memberEmail);
        var tenant = await TenantSeeding.CreateTenantAsync(fixture);
        await TenantSeeding.AddMembershipAsync(fixture, tenant.Id, memberId, TenantRole.Officer);

        var response = await fixture.ApiClient.SendAsync(
            CreateRequest(HttpMethod.Patch, $"/api/v1/t/{tenant.Slug}", memberToken, new { Name = "Hijacked" }));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
