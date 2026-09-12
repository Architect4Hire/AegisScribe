using AegisScribe.Domain.Business;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;
using FluentValidation;

namespace AegisScribe.Domain.Facade;

// Validation only — nothing here is cacheable: registration and password changes are writes, and the
// sign-in lookups must reflect the store as it is right now (a lockout or a revoked role can't wait out
// a cache entry). A ValidationException becomes a 400 in the global exception handler.
public class AuthFacade(
    IAuthBusiness business,
    IValidator<RegisterViewModel> registerValidator,
    IValidator<ChangePasswordViewModel> changePasswordValidator) : IAuthFacade
{
    public async Task<UserServiceModel> RegisterAsync(RegisterViewModel viewModel, CancellationToken ct)
    {
        await registerValidator.ValidateAndThrowAsync(viewModel, ct);
        return await business.RegisterAsync(viewModel, ct);
    }

    public async Task ChangePasswordAsync(ChangePasswordViewModel viewModel, CancellationToken ct)
    {
        await changePasswordValidator.ValidateAndThrowAsync(viewModel, ct);
        await business.ChangePasswordAsync(viewModel, ct);
    }

    public Task<SignInSubjectServiceModel?> ValidateCredentialsAsync(SignInViewModel viewModel, CancellationToken ct) =>
        business.ValidateCredentialsAsync(viewModel, ct);

    public Task<SignInSubjectServiceModel?> GetSignInSubjectAsync(string userId, CancellationToken ct) =>
        business.GetSignInSubjectAsync(userId, ct);
}
