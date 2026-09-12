using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;

namespace AegisScribe.Domain.Managers.Mappers;

public static class TenantMappers
{
    // Id is generated in Business, not read from the client — a tenant's identity isn't theirs to pick.
    public static Tenant ToEntity(this CreateTenantViewModel viewModel, Guid id) => new()
    {
        Id = id,
        Slug = viewModel.Slug,
        Name = viewModel.Name,
        TimeZoneId = viewModel.TimeZoneId,
    };

    public static TenantServiceModel ToServiceModel(this Tenant tenant) => new()
    {
        Id = tenant.Id,
        Slug = tenant.Slug,
        Name = tenant.Name,
        TimeZoneId = tenant.TimeZoneId,
    };
}
