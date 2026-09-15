using AegisScribe.Domain.Managers.Models.Identity;
using Microsoft.AspNetCore.Identity;

namespace AegisScribe.Domain.Data;

// UserManager/SignInManager take no CancellationToken, so each method checks it once up front
// rather than dropping it — an abandoned request still stops before touching the store.
public class UserRepository(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager) : IUserRepository
{
    public Task<IdentityResult> CreateAsync(ApplicationUser user, string password, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return userManager.CreateAsync(user, password);
    }

    public Task<ApplicationUser?> FindByEmailAsync(string email, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return userManager.FindByEmailAsync(email);
    }

    public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return userManager.FindByIdAsync(userId);
    }

    public Task<IdentityResult> ChangePasswordAsync(
        ApplicationUser user, string currentPassword, string newPassword, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return userManager.ChangePasswordAsync(user, currentPassword, newPassword);
    }

    public async Task<CredentialCheckResult> CheckPasswordSignInAsync(ApplicationUser user, string password, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var result = await signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        return result.Succeeded ? CredentialCheckResult.Success
            : result.IsLockedOut ? CredentialCheckResult.LockedOut
            : CredentialCheckResult.Invalid;
    }

    public Task<bool> CanSignInAsync(ApplicationUser user, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return signInManager.CanSignInAsync(user);
    }

    public async Task<IReadOnlyList<string>> GetRolesAsync(ApplicationUser user, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return [.. await userManager.GetRolesAsync(user)];
    }
}
