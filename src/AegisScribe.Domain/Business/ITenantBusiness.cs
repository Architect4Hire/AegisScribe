using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;

namespace AegisScribe.Domain.Business;

public interface ITenantBusiness
{
    Task<Guid?> ResolveForCurrentUserAsync(string slug, CancellationToken ct);

    Task<TenantRole?> GetRoleForCurrentUserAsync(Guid tenantId, CancellationToken ct);

    Task<SlugCheckServiceModel> CheckSlugAsync(SlugCheckViewModel viewModel, CancellationToken ct);

    Task<TenantServiceModel> CreateAsync(CreateTenantViewModel viewModel, CancellationToken ct);

    Task<TenantServiceModel> GetByIdAsync(Guid tenantId, CancellationToken ct);

    Task<TenantServiceModel> RenameAsync(Guid tenantId, RenameTenantViewModel viewModel, CancellationToken ct);
}
