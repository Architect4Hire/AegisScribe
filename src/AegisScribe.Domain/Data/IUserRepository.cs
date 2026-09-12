using AegisScribe.Domain.Managers.Models.Identity;
using Microsoft.AspNetCore.Identity;

namespace AegisScribe.Domain.Data;

// Identity's store APIs (UserManager, SignInManager) are data access, so they sit here exactly like
// a DbContext would — no controller, facade or business class holds one (add-endpoint skill).
public interface IUserRepository
{
    Task<IdentityResult> CreateAsync(ApplicationUser user, string password, CancellationToken ct);

    Task<ApplicationUser?> FindByEmailAsync(string email, CancellationToken ct);

    Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken ct);

    Task<IdentityResult> ChangePasswordAsync(
        ApplicationUser user, string currentPassword, string newPassword, CancellationToken ct);

    // Lockout-aware: a failure counts towards lockout, exactly as the interactive sign-in always has.
    Task<bool> CheckPasswordSignInAsync(ApplicationUser user, string password, CancellationToken ct);

    Task<bool> CanSignInAsync(ApplicationUser user, CancellationToken ct);

    Task<IReadOnlyList<string>> GetRolesAsync(ApplicationUser user, CancellationToken ct);
}
