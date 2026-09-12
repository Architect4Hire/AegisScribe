using AegisScribe.Domain.Managers.Models.Identity;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;

namespace AegisScribe.Domain.Managers.Mappers;

public static class UserMappers
{
    // The password is deliberately not part of the entity — it goes to the store separately, hashed.
    public static ApplicationUser ToEntity(this RegisterViewModel viewModel, DateTimeOffset createdAt) => new()
    {
        UserName = viewModel.Email,
        Email = viewModel.Email,
        DisplayName = viewModel.DisplayName,
        CreatedAt = createdAt,
    };

    public static UserServiceModel ToServiceModel(
        this ApplicationUser user, IReadOnlyList<TenantMembershipServiceModel> memberships) => new()
    {
        Id = user.Id,
        Email = user.Email!,
        DisplayName = user.DisplayName,
        CreatedAt = user.CreatedAt,
        Memberships = memberships,
    };
}
