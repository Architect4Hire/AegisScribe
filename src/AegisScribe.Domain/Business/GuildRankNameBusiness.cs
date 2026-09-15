using AegisScribe.Domain.Data;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;

namespace AegisScribe.Domain.Business;

// 7.5. One rule, and it is a rule rather than bookkeeping by the add-endpoint skill's test: delete the
// follows-guild check and an officer can name the ranks of a guild their community has nothing to do
// with — a write that should have been refused instead happens, and the row is then invisible because
// nothing joins to it.
public class GuildRankNameBusiness(IGuildRankNameDataLayer dataLayer) : IGuildRankNameBusiness
{
    // The range Blizzard's guild roster reports. Rejected as a field error rather than a 404, because
    // a rank outside 0-9 is a malformed request rather than a missing thing.
    private const int LowestRank = 0;
    private const int HighestRank = 9;

    public Task<IReadOnlyList<GuildRankNameServiceModel>> ListAsync(CancellationToken ct) =>
        dataLayer.ListAsync(ct);

    public async Task<bool> SetAsync(
        Guid guildId, int rank, SetGuildRankNameViewModel viewModel, CancellationToken ct)
    {
        if (rank is < LowestRank or > HighestRank)
        {
            throw new DomainValidationException(new Dictionary<string, string[]>
            {
                ["rank"] = [$"In-game ranks run from {LowestRank} to {HighestRank}."],
            });
        }

        // Query-filtered, so a guild another community follows reads as one this community does not —
        // which is the correct answer and also the cross-tenant refusal.
        if (!await dataLayer.FollowsGuildAsync(guildId, ct))
        {
            return false;
        }

        await dataLayer.SetAsync(guildId, rank, viewModel.Name, ct);

        return true;
    }
}
