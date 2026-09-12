using AegisScribe.Domain.Context;

namespace AegisScribe.Domain.Facade;

// Inherit this when a facade caches tenant-scoped ServiceModels. CacheKey(...) is already prefixed
// with the ambient tenant, so there is no way to build a bare (global-shaped) key from inside one of
// these — a bare tenant key would leak one community's data to another (tenancy.md).
public abstract class TenantScopedFacadeBase(ITenantContext tenantContext)
{
    protected string CacheKey(string key) => FacadeCacheKey.ForTenant(tenantContext, key);
}
