using AegisScribe.Domain.Managers.Models.Domain;

namespace AegisScribe.Domain.Facade;

public interface ITenantResolutionFacade
{
    // The tenant id for the route's slug when the current caller is a member of it; otherwise null.
    Task<Guid?> ResolveForCurrentUserAsync(string slug, CancellationToken ct);

    // The caller's role in an already-resolved tenant, or null if they aren't a member. Backs the
    // tenant policy handler (auth.md) — never the DbContext directly.
    Task<TenantRole?> GetRoleForCurrentUserAsync(Guid tenantId, CancellationToken ct);
}
