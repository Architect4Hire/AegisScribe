using AegisScribe.Domain.Context;
using AegisScribe.Domain.Facade;

namespace AegisScribe.Tests.Tenancy;

// tenancy.md: both directions are bugs — a bare tenant key leaks across communities, a
// tenant-prefixed global key fragments the shared cache N ways. Both paths under test.
public class FacadeCacheKeyTests
{
    [Fact]
    public void ForTenant_PrefixesWithTheAmbientTenant()
    {
        var tenantId = Guid.NewGuid();
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId);

        Assert.Equal($"t:{tenantId}:roster:1", FacadeCacheKey.ForTenant(tenantContext, "roster:1"));
    }

    [Fact]
    public void ForTenant_OutsideTenantScope_Throws()
    {
        // No default, no bare fallback — the same fail-closed behavior every other ambient-tenant
        // reader in this codebase has.
        var tenantContext = new TenantContext();

        Assert.Throws<InvalidOperationException>(() => FacadeCacheKey.ForTenant(tenantContext, "roster:1"));
    }

    [Fact]
    public void Global_IsNeverPrefixed()
    {
        Assert.Equal("char:emberwatch:thornwake", FacadeCacheKey.Global("char:emberwatch:thornwake"));
    }

    private sealed class FixtureTenantFacade(ITenantContext tenantContext) : TenantScopedFacadeBase(tenantContext)
    {
        public string BuildKey(string key) => CacheKey(key);
    }

    private sealed class FixtureGlobalFacade : GlobalFacadeBase
    {
        public string BuildKey(string key) => CacheKey(key);
    }

    [Fact]
    public void TenantScopedFacadeBase_BuildsATenantPrefixedKey()
    {
        var tenantId = Guid.NewGuid();
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId);
        var facade = new FixtureTenantFacade(tenantContext);

        Assert.Equal($"t:{tenantId}:events:1", facade.BuildKey("events:1"));
    }

    [Fact]
    public void GlobalFacadeBase_BuildsABareKey()
    {
        var facade = new FixtureGlobalFacade();

        Assert.Equal("item:19019", facade.BuildKey("item:19019"));
    }
}
