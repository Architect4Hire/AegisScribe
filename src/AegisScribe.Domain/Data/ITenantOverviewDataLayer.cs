namespace AegisScribe.Domain.Data;

/// <summary>The raw facts behind the first-run checklist, gathered in one logical read.</summary>
public interface ITenantOverviewDataLayer
{
    Task<TenantOverviewFacts> GetFactsAsync(Guid tenantId, CancellationToken ct);
}

/// <summary>
/// What four repositories and the Blizzard gateway between them know about a community's setup state.
/// </summary>
/// <remarks>
/// Internal to the data seam on purpose: it is not a ServiceModel and never goes on the wire. Business
/// turns it into one, which is what keeps the mapping step where the layering says it lives.
/// </remarks>
public record TenantOverviewFacts(
    bool HasLinkedGuild,
    int RankCount,
    int RosterCount,
    int MemberCount,
    bool BlizzardConfigured);
