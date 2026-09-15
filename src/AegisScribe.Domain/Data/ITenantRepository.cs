using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;

namespace AegisScribe.Domain.Data;

public interface ITenantRepository
{
    Task<Tenant?> FindBySlugAsync(string slug, CancellationToken ct);

    // Returns a bool, not the Tenant: an availability check must not be able to hand another
    // community's row back up the stack (2.7b — the endpoint answers "is this free" and nothing more).
    Task<bool> SlugExistsAsync(string slug, CancellationToken ct);

    Task<bool> IsMemberAsync(Guid tenantId, string userId, CancellationToken ct);

    Task<TenantRole?> GetRoleAsync(Guid tenantId, string userId, CancellationToken ct);

    Task<IReadOnlyList<TenantMembershipServiceModel>> GetMembershipsForUserAsync(string userId, CancellationToken ct);

    Task<Tenant?> FindByIdAsync(Guid id, CancellationToken ct);

    Task AddAsync(Tenant tenant, CancellationToken ct);

    Task AddMembershipAsync(TenantMembership membership, CancellationToken ct);

    Task UpdateNameAsync(Tenant tenant, CancellationToken ct);

    Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken ct);
}
