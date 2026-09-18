using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;

namespace AegisScribe.Domain.Data;

// Every method here runs under TenantRank's global query filter, so "this tenant's" is not something
// a caller can forget to ask for — there is no tenantId parameter anywhere in this interface, and an
// id belonging to another community simply does not resolve (tenancy.md).
public interface ITenantRankRepository
{
    // A list read projects straight to the outbound ServiceModel in SQL (add-endpoint skill).
    Task<IReadOnlyList<TenantRankServiceModel>> ListAsync(CancellationToken ct);

    // How many ranks this community has named — counted in SQL, because the first-run checklist wants
    // the number and not the ladder.
    Task<int> CountAsync(CancellationToken ct);

    Task<TenantRank?> FindAsync(Guid id, CancellationToken ct);

    // excludingId is how an update keeps its own name: without it, saving a rank unchanged would
    // collide with itself.
    Task<bool> NameExistsAsync(string name, Guid? excludingId, CancellationToken ct);

    // TenantId is stamped by the SaveChanges interceptor, never assigned here (tenancy.md).
    Task AddAsync(TenantRank rank, CancellationToken ct);

    Task UpdateAsync(TenantRank rank, CancellationToken ct);

    Task DeleteAsync(Guid id, CancellationToken ct);
}
