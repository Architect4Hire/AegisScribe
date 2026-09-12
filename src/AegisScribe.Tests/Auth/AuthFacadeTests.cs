using AegisScribe.Domain.Business;
using AegisScribe.Domain.Facade;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;
using AegisScribe.Domain.Managers.Validators;
using FluentValidation;
using NSubstitute;

namespace AegisScribe.Tests.Auth;

// Facade: real validators, mocked business. Nothing here is cached (writes and live sign-in lookups),
// so the facade's whole job is "validate, then delegate" — and "don't delegate when invalid".
// Passwords are short dummies: business is mocked, so nothing checks their strength.
public class AuthFacadeTests
{
    private readonly IAuthBusiness _business = Substitute.For<IAuthBusiness>();
    private readonly AuthFacade _facade;

    public AuthFacadeTests()
    {
        _facade = new AuthFacade(_business, new RegisterViewModelValidator(), new ChangePasswordViewModelValidator());
    }

    [Fact]
    public async Task Register_Valid_DelegatesAndReturnsTheServiceModel()
    {
        var viewModel = new RegisterViewModel { Email = "a@example.com", Password = "pw" };
        var expected = new UserServiceModel { Id = "u1", Email = "a@example.com" };
        _business.RegisterAsync(viewModel, Arg.Any<CancellationToken>()).Returns(expected);

        var result = await _facade.RegisterAsync(viewModel, CancellationToken.None);

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task Register_Invalid_ThrowsValidationException_AndNeverReachesBusiness()
    {
        var viewModel = new RegisterViewModel { Email = "not-an-email", Password = "" };

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _facade.RegisterAsync(viewModel, CancellationToken.None));

        Assert.Contains(ex.Errors, e => e.PropertyName == nameof(RegisterViewModel.Email));
        Assert.Contains(ex.Errors, e => e.PropertyName == nameof(RegisterViewModel.Password));
        await _business.DidNotReceiveWithAnyArgs().RegisterAsync(default!, default);
    }

    [Fact]
    public async Task ChangePassword_Valid_Delegates()
    {
        var viewModel = new ChangePasswordViewModel { Email = "a@example.com", CurrentPassword = "x", NewPassword = "y" };

        await _facade.ChangePasswordAsync(viewModel, CancellationToken.None);

        await _business.Received(1).ChangePasswordAsync(viewModel, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChangePassword_Invalid_ThrowsValidationException_AndNeverReachesBusiness()
    {
        var viewModel = new ChangePasswordViewModel { Email = "", CurrentPassword = "", NewPassword = "" };

        await Assert.ThrowsAsync<ValidationException>(() => _facade.ChangePasswordAsync(viewModel, CancellationToken.None));

        await _business.DidNotReceiveWithAnyArgs().ChangePasswordAsync(default!, default);
    }

    [Fact]
    public async Task ValidateCredentials_DelegatesWithoutValidating()
    {
        // A malformed credential is a failed sign-in (form re-renders), never a 400 — so no validator.
        var viewModel = new SignInViewModel { Email = "", Password = "" };

        var result = await _facade.ValidateCredentialsAsync(viewModel, CancellationToken.None);

        Assert.Null(result);
        await _business.Received(1).ValidateCredentialsAsync(viewModel, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSignInSubject_Delegates()
    {
        var expected = new SignInSubjectServiceModel { UserId = "u1", Roles = ["PlatformAdmin"] };
        _business.GetSignInSubjectAsync("u1", Arg.Any<CancellationToken>()).Returns(expected);

        Assert.Same(expected, await _facade.GetSignInSubjectAsync("u1", CancellationToken.None));
    }
}
