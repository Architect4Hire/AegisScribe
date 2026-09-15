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

// Through the endpoint: TenantsController/TenantController -> ITenantFacade -> ITenantBusiness ->
// ITenantDataLayer -> ITenantRepository -> SQL, plus the idempotency filter and the TenantOwner policy.
[Collection("AegisScribe API")]
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

    // Enums cross the wire as strings (api-contract.md), and the API is configured that way; the test
    // client needs the matching converter to read one back.
    private static readonly System.Text.Json.JsonSerializerOptions WireJson =
        new(System.Text.Json.JsonSerializerDefaults.Web)
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        };

    private static readonly SemaphoreSlim SharedCallerGate = new(1, 1);
    private static string? _sharedCallerToken;

    // Registering a user runs a full code+PKCE exchange, and registration is anonymous — so it lands
    // in the tightest rate-limit partition there is (Program.cs: 20/minute per IP). The cases below
    // only need *an* authenticated caller, not a distinct one, so they share a single registration
    // rather than spending that budget per test and starving the rest of the collection.
    private async Task<string> SharedCallerTokenAsync()
    {
        if (_sharedCallerToken is not null)
        {
            return _sharedCallerToken;
        }

        await SharedCallerGate.WaitAsync();
        try
        {
            return _sharedCallerToken ??= await RegisterAndGetTokenAsync();
        }
        finally
        {
            SharedCallerGate.Release();
        }
    }

    [Fact]
    public async Task CheckSlug_FreeSlug_IsAvailable()
    {
        var accessToken = await SharedCallerTokenAsync();
        var slug = TenantSeeding.UniqueSlug();

        var response = await fixture.ApiClient.SendAsync(
            CreateRequest(HttpMethod.Get, $"/api/v1/tenants/slug-check?slug={slug}", accessToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SlugCheckServiceModel>(WireJson);
        Assert.True(body!.Available);
        Assert.Equal(slug, body.Slug);
    }

    [Fact]
    public async Task CheckSlug_AfterTheSlugIsCreated_SaysTaken()
    {
        var accessToken = await SharedCallerTokenAsync();
        var slug = TenantSeeding.UniqueSlug();

        var created = await fixture.ApiClient.SendAsync(CreateRequest(
            HttpMethod.Post, "/api/v1/tenants", accessToken,
            new { Slug = slug, Name = "Emberwatch", TimeZoneId = "UTC" }));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var response = await fixture.ApiClient.SendAsync(
            CreateRequest(HttpMethod.Get, $"/api/v1/tenants/slug-check?slug={slug}", accessToken));

        var body = await response.Content.ReadFromJsonAsync<SlugCheckServiceModel>(WireJson);
        Assert.False(body!.Available);
        Assert.Equal(SlugCheckReason.Taken, body.Reason);
    }

    [Fact]
    public async Task CheckSlug_ByName_ReturnsTheDerivedSlug()
    {
        var accessToken = await SharedCallerTokenAsync();

        var response = await fixture.ApiClient.SendAsync(
            CreateRequest(HttpMethod.Get, "/api/v1/tenants/slug-check?name=Ashes%20of%20Dawn", accessToken));

        var body = await response.Content.ReadFromJsonAsync<SlugCheckServiceModel>(WireJson);
        Assert.Equal("ashes-of-dawn", body!.Slug);
    }

    [Fact]
    public async Task CheckSlug_ReservedSlug_IsAnAnswerNotAnError()
    {
        var accessToken = await SharedCallerTokenAsync();

        var response = await fixture.ApiClient.SendAsync(
            CreateRequest(HttpMethod.Get, "/api/v1/tenants/slug-check?slug=admin", accessToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SlugCheckServiceModel>(WireJson);
        Assert.False(body!.Available);
        Assert.Equal(SlugCheckReason.Reserved, body.Reason);
    }

    [Fact]
    public async Task CheckSlug_BothInputs_IsABadRequest()
    {
        var accessToken = await SharedCallerTokenAsync();

        var response = await fixture.ApiClient.SendAsync(
            CreateRequest(HttpMethod.Get, "/api/v1/tenants/slug-check?name=Ashes&slug=ashes", accessToken));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CheckSlug_Unauthenticated_Unauthorized()
    {
        var response = await fixture.ApiClient.GetAsync("/api/v1/tenants/slug-check?slug=anything");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_NoSlug_DerivesOneFromTheName()
    {
        var accessToken = await SharedCallerTokenAsync();
        // Unique, and deliberately full of things the derivation has to fold: accents, punctuation
        // and doubled separators.
        var name = $"Ashés  of -- Dawn {Guid.NewGuid():N}";

        var response = await fixture.ApiClient.SendAsync(CreateRequest(
            HttpMethod.Post, "/api/v1/tenants", accessToken,
            new { Name = name, TimeZoneId = "UTC" }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TenantServiceModel>();
        Assert.StartsWith("ashes-of-dawn-", body!.Slug);
    }

    [Fact]
    public async Task Create_NoSlug_AndANameThatFoldsToNothing_IsAValidationProblem()
    {
        var accessToken = await SharedCallerTokenAsync();

        var response = await fixture.ApiClient.SendAsync(CreateRequest(
            HttpMethod.Post, "/api/v1/tenants", accessToken,
            new { Name = "잿빛 여명", TimeZoneId = "UTC" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonDocument>();
        Assert.True(problem!.RootElement.GetProperty("errors").TryGetProperty("Slug", out _));
    }

    [Fact]
    public async Task Create_ReservedSlug_IsAValidationProblem()
    {
        var accessToken = await SharedCallerTokenAsync();

        var response = await fixture.ApiClient.SendAsync(CreateRequest(
            HttpMethod.Post, "/api/v1/tenants", accessToken,
            new { Slug = "admin", Name = "Admin", TimeZoneId = "UTC" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonDocument>();
        Assert.True(problem!.RootElement.GetProperty("errors").TryGetProperty("Slug", out _));
    }

    [Fact]
    public async Task Create_UnrecognizedTimeZone_IsAValidationProblem()
    {
        var accessToken = await SharedCallerTokenAsync();

        var response = await fixture.ApiClient.SendAsync(CreateRequest(
            HttpMethod.Post, "/api/v1/tenants", accessToken,
            new { Slug = TenantSeeding.UniqueSlug(), Name = "Emberwatch", TimeZoneId = "Middle/Earth" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonDocument>();
        Assert.True(problem!.RootElement.GetProperty("errors").TryGetProperty("TimeZoneId", out _));
    }

    // The race the pre-check cannot close: both requests see a free slug, one loses at the unique
    // index. A mocked transaction only proves a commit was asked for, so this runs against the real
    // database (add-endpoint skill).
    [Fact]
    public async Task Create_ConcurrentSameSlug_LosesWithA409_NotA500()
    {
        var accessToken = await SharedCallerTokenAsync();
        var slug = TenantSeeding.UniqueSlug();

        var bodies = Enumerable.Range(0, 6).Select(_ => new { Slug = slug, Name = "Emberwatch", TimeZoneId = "UTC" });
        var responses = await Task.WhenAll(bodies.Select(body =>
            fixture.ApiClient.SendAsync(CreateRequest(HttpMethod.Post, "/api/v1/tenants", accessToken, body))));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));

        // Every loser is either the pre-check's 400 (it read the committed row) or the index's 409 (it
        // did not). Never a 500, which is what this endpoint did before the translation existed.
        Assert.All(
            responses.Where(r => r.StatusCode != HttpStatusCode.Created),
            r => Assert.True(
                r.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict,
                $"Expected 400 or 409 for a losing racer, got {(int)r.StatusCode}."));

        foreach (var conflict in responses.Where(r => r.StatusCode == HttpStatusCode.Conflict))
        {
            using var problem = await conflict.Content.ReadFromJsonAsync<System.Text.Json.JsonDocument>();
            Assert.Equal(
                "https://api.aegisscribe.com/problems/tenant-slug-taken",
                problem!.RootElement.GetProperty("type").GetString());
        }
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
