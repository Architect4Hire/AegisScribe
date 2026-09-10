using AegisScribe.ApiService.Managers.Models.Identity;
using AegisScribe.ApiService.Managers.Models.ServiceModels;
using AegisScribe.ApiService.Managers.Models.ViewModels;
using Microsoft.AspNetCore.Identity;

namespace AegisScribe.ApiService.Auth;

public class AuthService(UserManager<ApplicationUser> userManager) : IAuthService
{
    public async Task<AuthOperationResult<UserServiceModel>> RegisterAsync(RegisterViewModel viewModel, CancellationToken ct)
    {
        var user = new ApplicationUser
        {
            UserName = viewModel.Email,
            Email = viewModel.Email,
            DisplayName = viewModel.DisplayName,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var result = await userManager.CreateAsync(user, viewModel.Password);
        if (!result.Succeeded)
        {
            return AuthOperationResult<UserServiceModel>.Failure(ToErrorDictionary(result));
        }

        return AuthOperationResult<UserServiceModel>.Success(new UserServiceModel
        {
            Id = user.Id,
            Email = user.Email!,
            DisplayName = user.DisplayName,
            CreatedAt = user.CreatedAt,
        });
    }

    public async Task<AuthOperationResult> ChangePasswordAsync(ChangePasswordViewModel viewModel, CancellationToken ct)
    {
        var user = await userManager.FindByEmailAsync(viewModel.Email);
        if (user is null)
        {
            return AuthOperationResult.Failure(GenericCredentialsError());
        }

        var result = await userManager.ChangePasswordAsync(user, viewModel.CurrentPassword, viewModel.NewPassword);
        if (!result.Succeeded)
        {
            // Wrong current password is reported the same way as "user not found" above, so this
            // endpoint never confirms or denies whether an email has an account.
            var errors = result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.PasswordMismatch))
                ? GenericCredentialsError()
                : ToErrorDictionary(result);
            return AuthOperationResult.Failure(errors);
        }

        return AuthOperationResult.Success();
    }

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
