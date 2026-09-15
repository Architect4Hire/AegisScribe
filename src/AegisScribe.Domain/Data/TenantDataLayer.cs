using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;

namespace AegisScribe.Domain.Data;

// Pass-throughs: each lookup is one repository call. Kept as the seam Business depends on.
public class TenantDataLayer(ITenantRepository repository) : ITenantDataLayer
{
    public Task<Tenant?> FindBySlugAsync(string slug, CancellationToken ct) =>
        repository.FindBySlugAsync(slug, ct);

    public Task<bool> SlugExistsAsync(string slug, CancellationToken ct) =>
        repository.SlugExistsAsync(slug, ct);

    public Task<bool> IsMemberAsync(Guid tenantId, string userId, CancellationToken ct) =>
        repository.IsMemberAsync(tenantId, userId, ct);

    public Task<TenantRole?> GetRoleAsync(Guid tenantId, string userId, CancellationToken ct) =>
        repository.GetRoleAsync(tenantId, userId, ct);

    public Task<IReadOnlyList<TenantMembershipServiceModel>> GetMembershipsForUserAsync(string userId, CancellationToken ct) =>
        repository.GetMembershipsForUserAsync(userId, ct);

    public Task<Tenant?> FindByIdAsync(Guid id, CancellationToken ct) =>
        repository.FindByIdAsync(id, ct);

    // The one composed write: create-tenant-plus-owner-membership is two rows and must commit atomically
    // (add-endpoint skill) — handed to the repository as a callback rather than two separate calls.
    public Task<Tenant> CreateAsync(Tenant tenant, TenantMembership ownerMembership, CancellationToken ct) =>
        repository.ExecuteInTransactionAsync(async token =>
        {
            await repository.AddAsync(tenant, token);
            await repository.AddMembershipAsync(ownerMembership, token);
            return tenant;
        }, ct);

    public async Task<Tenant> RenameAsync(Tenant tenant, CancellationToken ct)
    {
        await repository.UpdateNameAsync(tenant, ct);
        return tenant;
    }
}
