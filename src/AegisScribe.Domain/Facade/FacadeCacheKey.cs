using AegisScribe.Domain.Context;

namespace AegisScribe.Domain.Facade;

// The cache-key convention (tenancy.md): a tenant-scoped ServiceModel's key starts with the tenant; a
// global reference ServiceModel's key does not. Both directions are bugs — a bare tenant key leaks one
// community's data to another, a tenant-prefixed global key fragments the shared cache N ways.
public static class FacadeCacheKey
{
    // Takes ITenantContext, never a bare Guid — the tenant segment can only ever be the ambient,
    // resolved caller's tenant (and throws, via ITenantContext, if called outside tenant scope).
    // There is no parameter here a caller could fill with the wrong tenant or a client-supplied id.
    public static string ForTenant(ITenantContext tenantContext, string key) =>
        $"t:{tenantContext.TenantId}:{key}";

    // Deliberately takes nothing but the key — there is no tenant-shaped parameter to reach for by
    // mistake, so a global cache read can't accidentally acquire a tenant prefix.
    public static string Global(string key) => key;
}
