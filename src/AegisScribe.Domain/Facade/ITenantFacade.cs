using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;

namespace AegisScribe.Domain.Facade;

public interface ITenantFacade
{
    Task<TenantServiceModel> CreateAsync(CreateTenantViewModel viewModel, CancellationToken ct);

    Task<TenantServiceModel> GetByIdAsync(Guid tenantId, CancellationToken ct);

    Task<TenantServiceModel> RenameAsync(Guid tenantId, RenameTenantViewModel viewModel, CancellationToken ct);
}
