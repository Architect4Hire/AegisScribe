using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;

namespace AegisScribe.Domain.Facade;

public interface IAuthFacade
{
    Task<UserServiceModel> RegisterAsync(RegisterViewModel viewModel, CancellationToken ct);

    Task ChangePasswordAsync(ChangePasswordViewModel viewModel, CancellationToken ct);

    // Behind connect/authorize's sign-in form: who to mint a token for, or why the form re-renders.
    Task<SignInAttemptServiceModel> ValidateCredentialsAsync(SignInViewModel viewModel, CancellationToken ct);

    // Behind connect/token's code and refresh grants: the subject as it stands now, or null to refuse.
    Task<SignInSubjectServiceModel?> GetSignInSubjectAsync(string userId, CancellationToken ct);
}
