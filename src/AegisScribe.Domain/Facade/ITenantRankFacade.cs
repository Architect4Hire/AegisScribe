using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;

namespace AegisScribe.Domain.Facade;

public interface ITenantRankFacade
{
    Task<IReadOnlyList<TenantRankServiceModel>> ListAsync(CancellationToken ct);

    Task<TenantRankServiceModel> CreateAsync(CreateRankViewModel viewModel, CancellationToken ct);

    Task<TenantRankServiceModel?> UpdateAsync(Guid rankId, UpdateRankViewModel viewModel, CancellationToken ct);

    Task DeleteAsync(Guid rankId, CancellationToken ct);
}
