using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;

namespace AegisScribe.Domain.Data;

// Mostly pass-throughs — each rank operation is one repository call with no transaction boundary to own
// — kept as the seam Business depends on.
//
// CountRankHoldersAsync is the one genuine composition: it reads the ROSTER table on the rank
// vertical's behalf, so TenantRankRepository keeps to the table it owns and the roster's own query
// filter still scopes the count to this community.
public class TenantRankDataLayer(
    ITenantRankRepository repository,
    IRosterEntryRepository rosterEntries) : ITenantRankDataLayer
{
    public Task<int> CountRankHoldersAsync(Guid rankId, CancellationToken ct) =>
        rosterEntries.CountByRankAsync(rankId, ct);

    public Task<IReadOnlyList<TenantRankServiceModel>> ListAsync(CancellationToken ct) =>
        repository.ListAsync(ct);

    public Task<TenantRank?> FindAsync(Guid id, CancellationToken ct) =>
        repository.FindAsync(id, ct);

    public Task<bool> NameExistsAsync(string name, Guid? excludingId, CancellationToken ct) =>
        repository.NameExistsAsync(name, excludingId, ct);

    public Task AddAsync(TenantRank rank, CancellationToken ct) =>
        repository.AddAsync(rank, ct);

    public Task UpdateAsync(TenantRank rank, CancellationToken ct) =>
        repository.UpdateAsync(rank, ct);

    public Task DeleteAsync(Guid id, CancellationToken ct) =>
        repository.DeleteAsync(id, ct);
}
