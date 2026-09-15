using AegisScribe.Domain.Data;
using AegisScribe.Domain.Managers.Mappers;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;

namespace AegisScribe.Domain.Business;

// 7.1. No tenant id anywhere in this class: the rank ladder it reads and writes is whichever
// community the route resolved, enforced by the query filter one layer down (tenancy.md).
public class TenantRankBusiness(ITenantRankDataLayer dataLayer) : ITenantRankBusiness
{
    public Task<IReadOnlyList<TenantRankServiceModel>> ListAsync(CancellationToken ct) =>
        dataLayer.ListAsync(ct);

    public async Task<TenantRankServiceModel> CreateAsync(CreateRankViewModel viewModel, CancellationToken ct)
    {
        // A rule, not bookkeeping, by the add-endpoint skill's own test: delete this read and an
        // officer ends up with two ranks they cannot tell apart — a write that should have been
        // refused wasn't, rather than an answer that is merely stale.
        if (await dataLayer.NameExistsAsync(viewModel.Name, excludingId: null, ct))
        {
            throw new RankNameTakenException(viewModel.Name);
        }

        var rank = viewModel.ToEntity(Guid.NewGuid());

        await dataLayer.AddAsync(rank, ct);

        return rank.ToServiceModel();
    }

    public async Task<TenantRankServiceModel?> UpdateAsync(
        Guid rankId, UpdateRankViewModel viewModel, CancellationToken ct)
    {
        var rank = await dataLayer.FindAsync(rankId, ct);

        if (rank is null)
        {
            return null;
        }

        // Excluding the row being edited, so saving a rank with its own name unchanged — which is what
        // a recolour or a reorder through a full-shape PUT does — is not a collision with itself.
        if (await dataLayer.NameExistsAsync(viewModel.Name, excludingId: rankId, ct))
        {
            throw new RankNameTakenException(viewModel.Name);
        }

        viewModel.Apply(rank);

        await dataLayer.UpdateAsync(rank, ct);

        return rank.ToServiceModel();
    }

    public async Task DeleteAsync(Guid rankId, CancellationToken ct)
    {
        // A rule, not bookkeeping (7.2): delete this check and an officer tidying up a ladder silently
        // un-ranks everyone who held it — an action that should have been refused instead happens, and
        // nothing in the 204 says so.
        var holders = await dataLayer.CountRankHoldersAsync(rankId, ct);

        if (holders > 0)
        {
            throw new RankInUseException(holders);
        }

        // Still no existence check, and that is still the point: DELETE is idempotent
        // (api-contract.md), so a rank that is already gone — or that was never this community's to
        // begin with — is a satisfied intent, not an error. Both cases have zero holders and fall
        // through to a delete that matches no rows. The query filter is what makes the second case a
        // no-op rather than a cross-tenant delete, which is what the two-tenant test pins down.
        await dataLayer.DeleteAsync(rankId, ct);
    }
}
