using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;

namespace AegisScribe.Domain.Managers.Mappers;

public static class TenantMappers
{
    // Id is generated in Business, not read from the client — a tenant's identity isn't theirs to pick.
    // The slug is passed in for the same reason it is no longer read off the view model: it may have
    // been derived from the name rather than supplied, and resolving which is Business's call.
    public static Tenant ToEntity(this CreateTenantViewModel viewModel, Guid id, string slug) => new()
    {
        Id = id,
        Slug = slug,
        Name = viewModel.Name,
        TimeZoneId = viewModel.TimeZoneId,
    };

    public static TenantServiceModel ToServiceModel(this Tenant tenant) => new()
    {
        Id = tenant.Id,
        Slug = tenant.Slug,
        Name = tenant.Name,
        TimeZoneId = tenant.TimeZoneId,
        AcceptsJoinRequests = tenant.AcceptsJoinRequests,
    };
}
