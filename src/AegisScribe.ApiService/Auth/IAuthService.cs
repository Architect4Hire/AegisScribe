using AegisScribe.ApiService.Managers.Models.ServiceModels;
using AegisScribe.ApiService.Managers.Models.ViewModels;

namespace AegisScribe.ApiService.Auth;

public interface IAuthService
{
    Task<AuthOperationResult<UserServiceModel>> RegisterAsync(RegisterViewModel viewModel, CancellationToken ct);

    Task<AuthOperationResult> ChangePasswordAsync(ChangePasswordViewModel viewModel, CancellationToken ct);
}
