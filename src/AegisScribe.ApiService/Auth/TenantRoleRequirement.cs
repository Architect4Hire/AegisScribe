using AegisScribe.Domain.Managers.Models.Domain;
using Microsoft.AspNetCore.Authorization;

namespace AegisScribe.ApiService.Auth;

// One requirement, parameterized by the minimum rank — TenantMember/TenantOfficer/TenantOwner are the
// same requirement type at three different MinimumRole values, so no endpoint lists three roles.
public class TenantRoleRequirement(TenantRole minimumRole) : IAuthorizationRequirement
{
    public TenantRole MinimumRole { get; } = minimumRole;
}
