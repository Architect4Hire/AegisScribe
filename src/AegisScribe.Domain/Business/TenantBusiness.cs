using AegisScribe.Domain.Context;
using AegisScribe.Domain.Data;
using AegisScribe.Domain.Managers.Mappers;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;

namespace AegisScribe.Domain.Business;

public class TenantBusiness(ITenantDataLayer dataLayer, ICurrentUser currentUser, TimeProvider timeProvider) : ITenantBusiness
{
    // The tenant id when the caller is a member of the tenant named by the route slug; otherwise null.
    // "No such tenant" and "tenant exists but you're not a member" MUST be indistinguishable — both
    // null, so both become the same 404; a difference here would confirm the tenant exists
    // (tenancy.md). That is a domain rule, which is why it lives here and not in the middleware.
    public async Task<Guid?> ResolveForCurrentUserAsync(string slug, CancellationToken ct)
    {
        var userId = currentUser.UserId;
        if (userId is null)
        {
            return null;
        }

        var tenant = await dataLayer.FindBySlugAsync(slug, ct);
        if (tenant is null)
        {
            return null;
        }

        return await dataLayer.IsMemberAsync(tenant.Id, userId, ct) ? tenant.Id : null;
    }

    // Backs the tenant role authorization handler. tenantId is already resolved (the ambient
    // ITenantContext), so this skips straight to the membership lookup — no slug involved.
    public Task<TenantRole?> GetRoleForCurrentUserAsync(Guid tenantId, CancellationToken ct)
    {
        var userId = currentUser.UserId;
        return userId is null
            ? Task.FromResult<TenantRole?>(null)
            : dataLayer.GetRoleAsync(tenantId, userId, ct);
    }

    // Slug uniqueness is checked here rather than left to the DB's unique index alone, so a taken slug
    // reads as a normal domain rejection (the same DomainValidationException shape RegisterAsync uses
    // for a duplicate email) instead of a raw constraint-violation 500.
    public async Task<TenantServiceModel> CreateAsync(CreateTenantViewModel viewModel, CancellationToken ct)
    {
        var userId = currentUser.UserId ?? throw new AuthenticationRequiredException();

        if (await dataLayer.FindBySlugAsync(viewModel.Slug, ct) is not null)
        {
            throw new DomainValidationException(new Dictionary<string, string[]>
            {
                ["Slug"] = ["This slug is already taken."],
            });
        }

        var tenant = viewModel.ToEntity(Guid.NewGuid());
        var ownerMembership = new TenantMembership
        {
            TenantId = tenant.Id,
            UserId = userId,
            Role = TenantRole.Owner,
            JoinedAt = timeProvider.GetUtcNow(),
        };

        var created = await dataLayer.CreateAsync(tenant, ownerMembership, ct);
        return created.ToServiceModel();
    }

    // Not-found isn't a real path here — the caller only reaches this after TenantResolutionMiddleware
    // has already proven the tenant exists (tenancy.md), so a missing row would be a bug, not a 404.
    public async Task<TenantServiceModel> GetByIdAsync(Guid tenantId, CancellationToken ct)
    {
        var tenant = await dataLayer.FindByIdAsync(tenantId, ct)
            ?? throw new InvalidOperationException($"Tenant '{tenantId}' was resolved but no longer exists.");
        return tenant.ToServiceModel();
    }

    public async Task<TenantServiceModel> RenameAsync(Guid tenantId, RenameTenantViewModel viewModel, CancellationToken ct)
    {
        var tenant = await dataLayer.FindByIdAsync(tenantId, ct)
            ?? throw new InvalidOperationException($"Tenant '{tenantId}' was resolved but no longer exists.");

        tenant.Name = viewModel.Name;
        var renamed = await dataLayer.RenameAsync(tenant, ct);
        return renamed.ToServiceModel();
    }
}
