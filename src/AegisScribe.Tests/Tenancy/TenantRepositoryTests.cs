using AegisScribe.Domain.Data;
using AegisScribe.Tests.Auth;
using AegisScribe.Tests.Gateway;

namespace AegisScribe.Tests.Tenancy;

// Repository: against the real SQL Server the AppHost runs — the lookups tenant resolution depends on,
// asserted below the layers that consume them. One registration for a real user id (memberships carry
// an FK to AspNetUsers); everything else is direct database work, so this adds almost nothing to the
// collection's anonymous rate-limit budget.
[Collection("AegisScribe API")]
public class TenantRepositoryTests(AegisScribeAppFixture fixture)
{
    [Fact]
    public async Task FindBySlug_ReturnsTheTenant_AndNullForAnUnknownSlug()
    {
        var tenant = await TenantSeeding.CreateTenantAsync(fixture);
        await using var db = await TenantSeeding.OpenDbContextAsync(fixture);
        var repository = new TenantRepository(db);

        var found = await repository.FindBySlugAsync(tenant.Slug, CancellationToken.None);
        var missing = await repository.FindBySlugAsync(TenantSeeding.UniqueSlug(), CancellationToken.None);

        Assert.Equal(tenant.Id, found?.Id);
        Assert.Null(missing);
    }

    [Fact]
    public async Task IsMember_IsTrueOnlyForThatUserInThatTenant()
    {
        var (_, userId) = await GatewayLoginFlow.RegisterUserWithIdAsync(fixture);
        var tenantA = await TenantSeeding.CreateTenantAsync(fixture);
        var tenantB = await TenantSeeding.CreateTenantAsync(fixture);
        await TenantSeeding.AddMembershipAsync(fixture, tenantA.Id, userId);

        await using var db = await TenantSeeding.OpenDbContextAsync(fixture);
        var repository = new TenantRepository(db);

        Assert.True(await repository.IsMemberAsync(tenantA.Id, userId, CancellationToken.None));
        Assert.False(await repository.IsMemberAsync(tenantB.Id, userId, CancellationToken.None));
        Assert.False(await repository.IsMemberAsync(tenantA.Id, Guid.NewGuid().ToString(), CancellationToken.None));
    }
}
