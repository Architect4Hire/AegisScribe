using AegisScribe.Domain.Business;
using AegisScribe.Domain.Managers.Models.ServiceModels;

namespace AegisScribe.Domain.Facade;

// No ViewModel, so nothing to validate — the only input is the resolved tenant.
//
// **Deliberately not cached**, and this one is worth stating because it looks like an obvious
// candidate. The whole purpose of this read is to tell an officer whether the thing they just did
// landed: they name their first rank, come back, and the checklist must say so. A five-minute Redis
// entry would answer "you still have no ranks" to the person who just made one, which is a worse
// experience than the round trip it saves. The Redis ServiceModel cache is for things that stay true
// for minutes (add-endpoint skill); a setup checklist is the opposite by construction.
//
// Caching it would also mean invalidating from four unrelated features — rank CRUD, roster writes,
// guild linking and every membership change — and an invalidation anybody can forget is how a cache
// starts lying.
public class TenantOverviewFacade(ITenantOverviewBusiness business) : ITenantOverviewFacade
{
    public Task<TenantOverviewServiceModel> GetAsync(Guid tenantId, CancellationToken ct) =>
        business.GetAsync(tenantId, ct);
}
