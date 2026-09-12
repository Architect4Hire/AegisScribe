using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;

namespace AegisScribe.Domain.Data;

public interface ITenantDataLayer
{
    Task<Tenant?> FindBySlugAsync(string slug, CancellationToken ct);

    Task<bool> IsMemberAsync(Guid tenantId, string userId, CancellationToken ct);

    Task<TenantRole?> GetRoleAsync(Guid tenantId, string userId, CancellationToken ct);

    Task<IReadOnlyList<TenantMembershipServiceModel>> GetMembershipsForUserAsync(string userId, CancellationToken ct);

    Task<Tenant?> FindByIdAsync(Guid id, CancellationToken ct);

    Task<Tenant> CreateAsync(Tenant tenant, TenantMembership ownerMembership, CancellationToken ct);

    Task<Tenant> RenameAsync(Tenant tenant, CancellationToken ct);
}
