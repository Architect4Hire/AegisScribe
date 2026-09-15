using System.Net;
using System.Net.Http.Json;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Tests.Auth;

namespace AegisScribe.Tests.Tenancy;

// Proves the harness works against 2.7's endpoints — the only tenant-scoped routes that exist so far.
// Reuses the "Tenant CRUD" collection since it's the same endpoints TenantEndpointTests already
// exercises there; no need for a third AppHost just for this.
[Collection("AegisScribe API")]
public class TwoTenantFixtureTests(AegisScribeAppFixture fixture)
{
    [Fact]
    public async Task ASeesItsOwnTenant_BDoesNot()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);

        var aReadsOwn = await scenario.TenantA.SendAsync(HttpMethod.Get, $"/api/v1/t/{scenario.TenantA.Tenant.Slug}");
        Assert.Equal(HttpStatusCode.OK, aReadsOwn.StatusCode);
        var aBody = await aReadsOwn.Content.ReadFromJsonAsync<TenantServiceModel>();
        Assert.Equal(scenario.TenantA.Tenant.Id, aBody!.Id);

        // Non-member gets 404, never 403 — a 403 would confirm the tenant exists (tenancy.md).
        var bReadsA = await scenario.TenantB.SendAsync(HttpMethod.Get, $"/api/v1/t/{scenario.TenantA.Tenant.Slug}");
        Assert.Equal(HttpStatusCode.NotFound, bReadsA.StatusCode);
    }

    [Fact]
    public async Task ATenantScopedWrite_IsUnreachableFromTheOtherTenant()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);

        // B can't even attempt to mutate A's row — there's no TenantId to smuggle in this endpoint
        // shape at all (tenantSlug never binds into a ViewModel), so the write is refused before it
        // could ever run, not merely rejected after running.
        var bWritesA = await scenario.TenantB.SendAsync(
            HttpMethod.Patch, $"/api/v1/t/{scenario.TenantA.Tenant.Slug}", new { Name = "Hijacked" });

        Assert.Equal(HttpStatusCode.NotFound, bWritesA.StatusCode);
    }

    [Fact]
    public async Task ARealWrite_DoesNotLeakAcrossTenants()
    {
        var scenario = await TwoTenantFixture.CreateAsync(fixture);

        var rename = await scenario.TenantA.SendAsync(
            HttpMethod.Patch, $"/api/v1/t/{scenario.TenantA.Tenant.Slug}", new { Name = "Ashenvale Reborn" });
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);

        // B still can't see it at all...
        var bReadsA = await scenario.TenantB.SendAsync(HttpMethod.Get, $"/api/v1/t/{scenario.TenantA.Tenant.Slug}");
        Assert.Equal(HttpStatusCode.NotFound, bReadsA.StatusCode);

        // ...while A sees its own write take effect, proving this was a real seed -> write -> read
        // round trip and not just a pair of read-only checks.
        var aReadsOwn = await scenario.TenantA.SendAsync(HttpMethod.Get, $"/api/v1/t/{scenario.TenantA.Tenant.Slug}");
        var aBody = await aReadsOwn.Content.ReadFromJsonAsync<TenantServiceModel>();
        Assert.Equal("Ashenvale Reborn", aBody!.Name);

        // Cache check (tenancy.md's fourth assertion) is not applicable yet: TenantFacade caches
        // nothing until 2.8 lands the tenant cache-key convention. Noted, not silently skipped.
    }
}
