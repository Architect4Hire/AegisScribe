using AegisScribe.Domain.Integration.Blizzard;

namespace AegisScribe.Domain.Data;

// One logical read composed from four repositories and one gateway property — which is precisely what
// a data layer is for (add-endpoint skill): Business asks once and does no sequencing.
//
// A vertical of its own rather than methods on TenantDataLayer, and that is a deliberate cost decision:
// TenantDataLayer is on the tenant-RESOLUTION path, so it is constructed for every request the API
// serves. Hanging four more scoped repositories off it would make every roster read pay for a screen
// that renders once in a community's life.
//
// Three of the four counts carry no tenantId because their entities are ITenantScoped and the global
// query filter has already confined them. TenantMembership is the documented exception — no filter, so
// its count names the tenant explicitly (tenancy.md).
public class TenantOverviewDataLayer(
    ITenantGuildRepository guilds,
    ITenantRankRepository ranks,
    IRosterEntryRepository roster,
    ITenantMembershipRepository memberships,
    IBlizzardGateway gateway) : ITenantOverviewDataLayer
{
    public async Task<TenantOverviewFacts> GetFactsAsync(Guid tenantId, CancellationToken ct)
    {
        // Sequential rather than a Task.WhenAll: these share one scoped DbContext, and EF Core forbids
        // concurrent operations on it. Four cheap COUNTs on indexed tenant columns; parallelising them
        // would trade a few milliseconds for an InvalidOperationException under load.
        var hasLinkedGuild = await guilds.AnyLinkedAsync(ct);
        var rankCount = await ranks.CountAsync(ct);
        var rosterCount = await roster.CountAsync(ct);
        var memberCount = await memberships.CountAsync(tenantId, ct);

        // A property, not a call — nothing reaches Blizzard here. Reading it through the gateway rather
        // than through BlizzardOptions is the established door: CharacterDataLayer already asks the
        // same question the same way before deciding whether a fetch is even possible.
        return new TenantOverviewFacts(
            hasLinkedGuild, rankCount, rosterCount, memberCount, gateway.IsConfigured);
    }
}
