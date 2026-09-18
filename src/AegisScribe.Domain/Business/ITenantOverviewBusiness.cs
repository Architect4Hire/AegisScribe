using AegisScribe.Domain.Managers.Models.ServiceModels;

namespace AegisScribe.Domain.Business;

public interface ITenantOverviewBusiness
{
    Task<TenantOverviewServiceModel> GetAsync(Guid tenantId, CancellationToken ct);
}
