using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;

namespace AegisScribe.Domain.Data;

public interface ITenantRepository
{
    Task<Tenant?> FindBySlugAsync(string slug, CancellationToken ct);

    Task<bool> IsMemberAsync(Guid tenantId, string userId, CancellationToken ct);

    Task<TenantRole?> GetRoleAsync(Guid tenantId, string userId, CancellationToken ct);

    Task<IReadOnlyList<TenantMembershipServiceModel>> GetMembershipsForUserAsync(string userId, CancellationToken ct);

    Task<Tenant?> FindByIdAsync(Guid id, CancellationToken ct);

    Task AddAsync(Tenant tenant, CancellationToken ct);

    Task AddMembershipAsync(TenantMembership membership, CancellationToken ct);

    Task UpdateNameAsync(Tenant tenant, CancellationToken ct);

    Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken ct);
}
