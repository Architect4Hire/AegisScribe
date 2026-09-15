using System.Net.Http.Headers;
using System.Net.Http.Json;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Tests.Auth;
using AegisScribe.Tests.Gateway;

namespace AegisScribe.Tests.Tenancy;

// The reusable two-tenant harness every isolation test builds on (tenancy.md → "Testing").
//
// Deliberately NOT an xUnit ICollectionFixture: sharing seeded tenants would let a write in one test
// leak into the next test's assertions. The expensive, read-only part — the AppHost, SQL Server, Redis
// — stays shared through the AegisScribeAppFixture passed in; only the two tenants are fresh.
public sealed class TwoTenantFixture
{
    public TenantSide TenantA { get; }
    public TenantSide TenantB { get; }

    public static async Task<TwoTenantFixture> CreateAsync(
        AegisScribeAppFixture fixture,
        TenantRole roleA = TenantRole.Owner,
        TenantRole roleB = TenantRole.Owner)
    {
        // Both sides get the SAME tenant name on purpose: a broken filter returning "a" tenant rather
        // than "the caller's" would still look plausible if their data merely differed. Only Slug and
        // Id distinguish them, which is what a real isolation bug has to be caught by.
        var sideA = await TenantSide.CreateAsync(fixture, "Ashenvale Raiders", roleA);
        var sideB = await TenantSide.CreateAsync(fixture, "Ashenvale Raiders", roleB);
        return new TwoTenantFixture(sideA, sideB);
    }

    private TwoTenantFixture(TenantSide tenantA, TenantSide tenantB)
    {
        TenantA = tenantA;
        TenantB = tenantB;
    }
}

// One tenant, one member user, one live bearer token, and the one way any test built on this fixture
// is allowed to reach the API.
public sealed class TenantSide
{
    public required Tenant Tenant { get; init; }
    public required string UserId { get; init; }
    public required string Email { get; init; }
    public required string AccessToken { get; init; }
    private HttpClient Client { get; init; } = null!;

    internal static async Task<TenantSide> CreateAsync(AegisScribeAppFixture fixture, string tenantName, TenantRole role)
    {
        var tenant = await TenantSeeding.CreateTenantAsync(fixture, name: tenantName);

        return await JoinAsync(fixture, tenant, role);
    }

    /// <summary>
    /// A second (third, fourth) person inside an EXISTING community — a fresh user and token, joined
    /// to the tenant at <paramref name="role"/>.
    /// </summary>
    /// <remarks>
    /// Isolation tests need two tenants; plenty of rules need two people in ONE tenant instead — "you
    /// may not release somebody else's claim", an officer acting on a member. A different axis from
    /// <see cref="TwoTenantFixture"/>, and this is the door to it.
    /// </remarks>
    internal static async Task<TenantSide> JoinAsync(
        AegisScribeAppFixture fixture, Tenant tenant, TenantRole role)
    {
        var (email, userId) = await GatewayLoginFlow.RegisterUserWithIdAsync(fixture);
        var (accessToken, _) = await DirectOAuthFlow.LoginAsBffClientAsync(fixture, email);
        await TenantSeeding.AddMembershipAsync(fixture, tenant.Id, userId, role);

        return new TenantSide
        {
            Tenant = tenant,
            UserId = userId,
            Email = email,
            AccessToken = accessToken,
            Client = fixture.ApiClient,
        };
    }

    // Every isolation assertion goes through the endpoint, never the repository (tenancy.md's
    // RESTRICTION on this exact harness) — this method is the only door.
    public Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return Client.SendAsync(request);
    }
}
