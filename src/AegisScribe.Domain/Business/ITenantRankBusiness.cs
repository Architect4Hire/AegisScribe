using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;

namespace AegisScribe.Domain.Business;

public interface ITenantRankBusiness
{
    Task<IReadOnlyList<TenantRankServiceModel>> ListAsync(CancellationToken ct);

    Task<TenantRankServiceModel> CreateAsync(CreateRankViewModel viewModel, CancellationToken ct);

    // Null when no rank with that id exists in THIS community — which covers both "no such rank" and
    // "that rank belongs to another community", deliberately indistinguishable. The controller turns
    // either into the same 404.
    Task<TenantRankServiceModel?> UpdateAsync(Guid rankId, UpdateRankViewModel viewModel, CancellationToken ct);

    Task DeleteAsync(Guid rankId, CancellationToken ct);
}
