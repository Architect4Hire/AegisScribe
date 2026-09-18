using AegisScribe.Domain.Data;
using AegisScribe.Domain.Managers.Models.ServiceModels;

namespace AegisScribe.Domain.Business;

// Facts in, ServiceModel out, and nothing else — no rule, because there is no refusal this read can
// make that the TenantOfficer policy has not already made.
//
// Note what is NOT here: any notion of "which step you are on". The checklist's ordering and
// completeness are the client's to derive from these five facts, and deliberately so. A server that
// returned a step number would be a server that has to be told when an officer deletes a rank.
public class TenantOverviewBusiness(ITenantOverviewDataLayer dataLayer) : ITenantOverviewBusiness
{
    public async Task<TenantOverviewServiceModel> GetAsync(Guid tenantId, CancellationToken ct)
    {
        var facts = await dataLayer.GetFactsAsync(tenantId, ct);

        return new TenantOverviewServiceModel
        {
            HasLinkedGuild = facts.HasLinkedGuild,
            RankCount = facts.RankCount,
            RosterCount = facts.RosterCount,
            MemberCount = facts.MemberCount,
            BlizzardConfigured = facts.BlizzardConfigured,
        };
    }
}
