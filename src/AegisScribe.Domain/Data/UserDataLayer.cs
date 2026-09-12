using AegisScribe.Domain.Managers.Models.Identity;
using Microsoft.AspNetCore.Identity;

namespace AegisScribe.Domain.Data;

// Pass-throughs today, and correct as such: each operation is one repository call. The seam is what
// lets a composed operation (say, register + create a first community) land here later without
// touching Business.
public class UserDataLayer(IUserRepository repository) : IUserDataLayer
{
    public Task<IdentityResult> CreateAsync(ApplicationUser user, string password, CancellationToken ct) =>
        repository.CreateAsync(user, password, ct);

    public Task<ApplicationUser?> FindByEmailAsync(string email, CancellationToken ct) =>
        repository.FindByEmailAsync(email, ct);

    public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken ct) =>
        repository.FindByIdAsync(userId, ct);

    public Task<IdentityResult> ChangePasswordAsync(
        ApplicationUser user, string currentPassword, string newPassword, CancellationToken ct) =>
        repository.ChangePasswordAsync(user, currentPassword, newPassword, ct);

    public Task<bool> CheckPasswordSignInAsync(ApplicationUser user, string password, CancellationToken ct) =>
        repository.CheckPasswordSignInAsync(user, password, ct);

    public Task<bool> CanSignInAsync(ApplicationUser user, CancellationToken ct) =>
        repository.CanSignInAsync(user, ct);

    public Task<IReadOnlyList<string>> GetRolesAsync(ApplicationUser user, CancellationToken ct) =>
        repository.GetRolesAsync(user, ct);
}
