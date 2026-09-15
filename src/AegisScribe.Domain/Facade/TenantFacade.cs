using AegisScribe.Domain.Business;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;
using FluentValidation;

namespace AegisScribe.Domain.Facade;

// The controller-facing Tenant CRUD facade — separate from ITenantResolutionFacade, which stays
// middleware/handler-only (resolving a slug or a role, never validating a write). Nothing here is
// cached yet: the tenant cache-key convention lands in 2.8, same as MeFacade's own deferral.
public class TenantFacade(
    ITenantBusiness business,
    IValidator<CreateTenantViewModel> createValidator,
    IValidator<RenameTenantViewModel> renameValidator,
    IValidator<SlugCheckViewModel> slugCheckValidator) : ITenantFacade
{
    // Deliberately NOT cached. The answer is "is this slug free right now", and a cached "available"
    // is exactly the stale read that turns into a 409 at submit time — a few milliseconds saved for a
    // worse failure later. The Redis ServiceModel cache is for things that stay true for minutes
    // (add-endpoint skill); this is not one of them.
    public async Task<SlugCheckServiceModel> CheckSlugAsync(SlugCheckViewModel viewModel, CancellationToken ct)
    {
        await slugCheckValidator.ValidateAndThrowAsync(viewModel, ct);
        return await business.CheckSlugAsync(viewModel, ct);
    }

    public async Task<TenantServiceModel> CreateAsync(CreateTenantViewModel viewModel, CancellationToken ct)
    {
        await createValidator.ValidateAndThrowAsync(viewModel, ct);
        return await business.CreateAsync(viewModel, ct);
    }

    public Task<TenantServiceModel> GetByIdAsync(Guid tenantId, CancellationToken ct) =>
        business.GetByIdAsync(tenantId, ct);

    public async Task<TenantServiceModel> RenameAsync(Guid tenantId, RenameTenantViewModel viewModel, CancellationToken ct)
    {
        await renameValidator.ValidateAndThrowAsync(viewModel, ct);
        return await business.RenameAsync(tenantId, viewModel, ct);
    }
}
