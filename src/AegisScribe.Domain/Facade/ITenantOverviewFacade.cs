using AegisScribe.Domain.Managers.Models.ServiceModels;

namespace AegisScribe.Domain.Facade;

public interface ITenantOverviewFacade
{
    Task<TenantOverviewServiceModel> GetAsync(Guid tenantId, CancellationToken ct);
}
