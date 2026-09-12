using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;

namespace AegisScribe.Domain.Business;

public interface IAuthBusiness
{
    Task<UserServiceModel> RegisterAsync(RegisterViewModel viewModel, CancellationToken ct);

    Task ChangePasswordAsync(ChangePasswordViewModel viewModel, CancellationToken ct);

    Task<SignInSubjectServiceModel?> ValidateCredentialsAsync(SignInViewModel viewModel, CancellationToken ct);

    Task<SignInSubjectServiceModel?> GetSignInSubjectAsync(string userId, CancellationToken ct);
}
