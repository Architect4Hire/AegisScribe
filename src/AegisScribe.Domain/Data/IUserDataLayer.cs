using AegisScribe.Domain.Managers.Models.Identity;
using Microsoft.AspNetCore.Identity;

namespace AegisScribe.Domain.Data;

public interface IUserDataLayer
{
    Task<IdentityResult> CreateAsync(ApplicationUser user, string password, CancellationToken ct);

    Task<ApplicationUser?> FindByEmailAsync(string email, CancellationToken ct);

    Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken ct);

    Task<IdentityResult> ChangePasswordAsync(
        ApplicationUser user, string currentPassword, string newPassword, CancellationToken ct);

    Task<CredentialCheckResult> CheckPasswordSignInAsync(ApplicationUser user, string password, CancellationToken ct);

    Task<bool> CanSignInAsync(ApplicationUser user, CancellationToken ct);

    Task<IReadOnlyList<string>> GetRolesAsync(ApplicationUser user, CancellationToken ct);
}
