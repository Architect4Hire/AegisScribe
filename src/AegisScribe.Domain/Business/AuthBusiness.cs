using AegisScribe.Domain.Data;
using AegisScribe.Domain.Managers.Mappers;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.Identity;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;
using Microsoft.AspNetCore.Identity;

namespace AegisScribe.Domain.Business;

public class AuthBusiness(IUserDataLayer dataLayer, TimeProvider timeProvider) : IAuthBusiness
{
    public async Task<UserServiceModel> RegisterAsync(RegisterViewModel viewModel, CancellationToken ct)
    {
        var user = viewModel.ToEntity(timeProvider.GetUtcNow());

        var result = await dataLayer.CreateAsync(user, viewModel.Password, ct);
        if (!result.Succeeded)
        {
            throw new DomainValidationException(ToErrorDictionary(result));
        }

        // A newly registered account has no tenant memberships yet — nothing to create one with.
        return user.ToServiceModel([]);
    }

    public async Task ChangePasswordAsync(ChangePasswordViewModel viewModel, CancellationToken ct)
    {
        // "No such account" and "wrong current password" are reported identically, so this endpoint
        // never confirms or denies whether an email has an account.
        var user = await dataLayer.FindByEmailAsync(viewModel.Email, ct)
            ?? throw new DomainValidationException(GenericCredentialsError());

        var result = await dataLayer.ChangePasswordAsync(user, viewModel.CurrentPassword, viewModel.NewPassword, ct);
        if (!result.Succeeded)
        {
            throw new DomainValidationException(
                result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.PasswordMismatch))
                    ? GenericCredentialsError()
                    : ToErrorDictionary(result));
        }
    }

    // Unknown email and wrong password both come back as a bare failed attempt, so neither confirms an
    // email is registered. LockedOut is the one exception — a narrow, deliberate leak of "this account
    // exists", accepted so a locked-out member sees why rather than re-guessing into a longer lockout.
    public async Task<SignInAttemptServiceModel> ValidateCredentialsAsync(SignInViewModel viewModel, CancellationToken ct)
    {
        var user = await dataLayer.FindByEmailAsync(viewModel.Email, ct);
        if (user is null)
        {
            return new SignInAttemptServiceModel();
        }

        var outcome = await dataLayer.CheckPasswordSignInAsync(user, viewModel.Password, ct);
        if (outcome != CredentialCheckResult.Success)
        {
            return new SignInAttemptServiceModel { IsLockedOut = outcome == CredentialCheckResult.LockedOut };
        }

        return new SignInAttemptServiceModel { Subject = await ToSubjectAsync(user, ct) };
    }

    // Re-read on every code redemption and refresh, so a deleted/locked user or a revoked PlatformAdmin
    // takes effect on the next token, not the next full sign-in (auth.md: resolved per request).
    public async Task<SignInSubjectServiceModel?> GetSignInSubjectAsync(string userId, CancellationToken ct)
    {
        var user = await dataLayer.FindByIdAsync(userId, ct);
        if (user is null || !await dataLayer.CanSignInAsync(user, ct))
        {
            return null;
        }

        return await ToSubjectAsync(user, ct);
    }

    private async Task<SignInSubjectServiceModel> ToSubjectAsync(ApplicationUser user, CancellationToken ct) => new()
    {
        UserId = user.Id,
        Roles = await dataLayer.GetRolesAsync(user, ct),
    };

    private static IDictionary<string, string[]> ToErrorDictionary(IdentityResult result) =>
        result.Errors
            .GroupBy(e => e.Code)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());

    private static IDictionary<string, string[]> GenericCredentialsError() =>
        new Dictionary<string, string[]>
        {
            ["InvalidCredentials"] = ["Email or current password is incorrect."],
        };
}
