using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;

namespace AegisScribe.Domain.Data;

public interface ITenantRankDataLayer
{
    Task<IReadOnlyList<TenantRankServiceModel>> ListAsync(CancellationToken ct);

    Task<TenantRank?> FindAsync(Guid id, CancellationToken ct);

    Task<bool> NameExistsAsync(string name, Guid? excludingId, CancellationToken ct);

    // How many of this community's roster entries still hold the rank. Composed from the roster
    // repository rather than the rank one, so each repository keeps to the table it owns.
    Task<int> CountRankHoldersAsync(Guid rankId, CancellationToken ct);

    Task AddAsync(TenantRank rank, CancellationToken ct);

    Task UpdateAsync(TenantRank rank, CancellationToken ct);

    Task DeleteAsync(Guid id, CancellationToken ct);
}
