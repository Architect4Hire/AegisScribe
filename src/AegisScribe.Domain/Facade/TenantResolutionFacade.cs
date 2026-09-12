using AegisScribe.Domain.Business;
using AegisScribe.Domain.Managers.Models.Domain;

namespace AegisScribe.Domain.Facade;

// Called by the tenant-resolution middleware on every tenant-scoped request, and by the tenant role
// authorization handler. The membership lookup is the obvious thing to cache once the 2.8 key
// convention exists — keyed per tenant AND user, and invalidated on any membership change, or a
// removed member keeps access until expiry.
public class TenantResolutionFacade(ITenantBusiness business) : ITenantResolutionFacade
{
    public Task<Guid?> ResolveForCurrentUserAsync(string slug, CancellationToken ct) =>
        business.ResolveForCurrentUserAsync(slug, ct);

    public Task<TenantRole?> GetRoleForCurrentUserAsync(Guid tenantId, CancellationToken ct) =>
        business.GetRoleForCurrentUserAsync(tenantId, ct);
}
