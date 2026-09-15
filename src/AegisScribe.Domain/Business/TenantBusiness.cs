using AegisScribe.Domain.Context;
using AegisScribe.Domain.Data;
using AegisScribe.Domain.Managers.Mappers;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;
using AegisScribe.Domain.Managers.Validators;

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

    // Answers the create form's per-keystroke question (5.6c) without making it a 400 factory: a
    // malformed or reserved slug is a normal answer here, not a validation failure. The reason never
    // names who holds a taken slug — any authenticated caller can reach this, and naming the holder
    // would turn it into a community-enumeration oracle (2.7b).
    public async Task<SlugCheckServiceModel> CheckSlugAsync(SlugCheckViewModel viewModel, CancellationToken ct)
    {
        // Exactly one of the two is set; the validator already enforced that.
        var slug = viewModel.Slug ?? TenantSlugRules.Derive(viewModel.Name);

        if (slug is null)
        {
            return Unavailable(null, SlugCheckReason.NotDerivable);
        }

        // Only reachable via an explicit ?slug= — a derived slug is well-formed by construction.
        if (!TenantSlugRules.IsWellFormed(slug))
        {
            return Unavailable(slug, SlugCheckReason.Invalid);
        }

        if (TenantSlugRules.IsReserved(slug))
        {
            return Unavailable(slug, SlugCheckReason.Reserved);
        }

        if (await dataLayer.SlugExistsAsync(slug, ct))
        {
            return Unavailable(slug, SlugCheckReason.Taken);
        }

        return new SlugCheckServiceModel { Slug = slug, Available = true, Reason = SlugCheckReason.Available };
    }

    private static SlugCheckServiceModel Unavailable(string? slug, SlugCheckReason reason) =>
        new() { Slug = slug, Available = false, Reason = reason };

    // Slug uniqueness is checked here rather than left to the DB's unique index alone, so a taken slug
    // reads as a normal domain rejection (the same DomainValidationException shape RegisterAsync uses
    // for a duplicate email) instead of a raw constraint-violation 500. The index is still the
    // authority: two callers can pass this check before either writes, and the repository translates
    // that race into a 409 (SlugTakenException). Two conditions, two answers, on purpose.
    public async Task<TenantServiceModel> CreateAsync(CreateTenantViewModel viewModel, CancellationToken ct)
    {
        var userId = currentUser.UserId ?? throw new AuthenticationRequiredException();

        // Derived when the caller omitted one (2.7b), so no client carries a copy of the rules. The
        // validator has already established that a null slug leaves a derivable name behind it.
        var slug = viewModel.Slug
            ?? TenantSlugRules.Derive(viewModel.Name)
            ?? throw new DomainValidationException(new Dictionary<string, string[]>
            {
                ["Slug"] = ["'Name' contains no characters usable in a URL — supply a slug as well."],
            });

        // Checked on the RESOLVED slug, which is the only place both paths meet. The validator can only
        // vet a slug the caller actually sent, so without this a community named "Admin" derives to
        // "admin" and takes a reserved route, while a caller who types slug=admin is refused — the hole
        // being in the derive path, which is the one the create screen defaults to (5.6c).
        if (TenantSlugRules.IsReserved(slug))
        {
            throw new DomainValidationException(new Dictionary<string, string[]>
            {
                ["Slug"] = ["This address is reserved — supply a different slug."],
            });
        }

        if (await dataLayer.SlugExistsAsync(slug, ct))
        {
            throw new DomainValidationException(new Dictionary<string, string[]>
            {
                ["Slug"] = ["This slug is already taken."],
            });
        }

        var tenant = viewModel.ToEntity(Guid.NewGuid(), slug);
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
