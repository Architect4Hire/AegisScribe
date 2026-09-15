using AegisScribe.Domain.Data;
using AegisScribe.Domain.Managers.Mappers;
using AegisScribe.Domain.Managers.Models.ServiceModels;

namespace AegisScribe.Domain.Business;

public interface ISyncBusiness
{
    Task<CharacterDetailServiceModel?> RefreshCharacterAsync(
        Guid tenantId, string region, string realmSlug, string name, CancellationToken ct);
}

// Tenant-TRIGGERED sync, and therefore a caller of the budget. Every other refresh path runs on the
// app's own schedule and is global; this one happens because a named officer in a named community asked
// for it, which is exactly the work external.md says must be charged to that community.
public class SyncBusiness(
    ITenantSyncBudget budget,
    ICharacterDataLayer characters) : ISyncBusiness
{
    // A refresh costs the character summary and the equipment: two Blizzard calls, charged whether or
    // not the stored row turns out to be current. That is the honest price — the officer asked us to go
    // and ask Blizzard, and we did.
    private const int CallsPerCharacterRefresh = 2;

    public async Task<CharacterDetailServiceModel?> RefreshCharacterAsync(
        Guid tenantId, string region, string realmSlug, string name, CancellationToken ct)
    {
        // Charged BEFORE the calls leave, not after. Charging on the way out would let a burst of
        // concurrent requests all pass the check and spend the budget several times over, which is the
        // starvation this exists to prevent. Throws when exhausted; the API turns that into a 429.
        await budget.ConsumeAsync(tenantId, CallsPerCharacterRefresh, ct);

        // Forced, not cache-first. The officer pressed refresh because they believe the stored row is
        // wrong, and answering from the store would make the button a lie — and would have charged them
        // for it.
        var result = await characters.RefreshCharacterAsync(region, realmSlug, name, ct);

        return result.Character?.ToServiceModel(result.IsDegraded);
    }
}
