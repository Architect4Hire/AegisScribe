namespace AegisScribe.Domain.Facade;

// Inherit this when a facade caches global reference ServiceModels. CacheKey(...) takes no tenant
// context at all, so there is no way to accidentally fragment the shared cache N ways by tenant
// (tenancy.md).
public abstract class GlobalFacadeBase
{
    protected string CacheKey(string key) => FacadeCacheKey.Global(key);
}
