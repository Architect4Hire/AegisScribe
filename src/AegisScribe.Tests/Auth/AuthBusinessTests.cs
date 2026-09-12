using AegisScribe.Domain.Business;
using AegisScribe.Domain.Data;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.Identity;
using AegisScribe.Domain.Managers.Models.ViewModels;
using Microsoft.AspNetCore.Identity;
using NSubstitute;

namespace AegisScribe.Tests.Auth;

// Business: mocked data layer. Covers the VM→entity translation, entity→ServiceModel mapping, and the
// one rule that matters most here — never revealing whether an email has an account. Passwords are
// short dummies: the store is mocked, so nothing checks their strength.
public class AuthBusinessTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);

    private readonly IUserDataLayer _dataLayer = Substitute.For<IUserDataLayer>();
    private readonly AuthBusiness _business;

    public AuthBusinessTests()
    {
        _business = new AuthBusiness(_dataLayer, new FixedTimeProvider(Now));
    }

    [Fact]
    public async Task Register_TranslatesTheViewModel_AndMapsTheStoredUser()
    {
        ApplicationUser? stored = null;
        _dataLayer.CreateAsync(Arg.Do<ApplicationUser>(u => stored = u), "pw", Arg.Any<CancellationToken>())
            .Returns(IdentityResult.Success);

        var result = await _business.RegisterAsync(
            new RegisterViewModel { Email = "a@example.com", Password = "pw", DisplayName = "Ann" },
            CancellationToken.None);

        Assert.NotNull(stored);
        Assert.Equal("a@example.com", stored!.UserName);
        Assert.Equal("a@example.com", stored.Email);
        Assert.Equal("Ann", stored.DisplayName);
        Assert.Equal(Now, stored.CreatedAt);

        Assert.Equal(stored.Id, result.Id);
        Assert.Equal("a@example.com", result.Email);
        Assert.Equal("Ann", result.DisplayName);
        Assert.Equal(Now, result.CreatedAt);
    }

    [Fact]
    public async Task Register_StoreRejects_ThrowsDomainValidationKeyedByIdentityCode()
    {
        _dataLayer.CreateAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(IdentityResult.Failed(new IdentityError { Code = "DuplicateEmail", Description = "Taken." }));

        var ex = await Assert.ThrowsAsync<DomainValidationException>(() => _business.RegisterAsync(
            new RegisterViewModel { Email = "a@example.com", Password = "pw" }, CancellationToken.None));

        Assert.Equal(["Taken."], ex.Errors["DuplicateEmail"]);
    }

    [Fact]
    public async Task ChangePassword_UnknownEmail_AndWrongPassword_AreIndistinguishable()
    {
        var viewModel = new ChangePasswordViewModel { Email = "a@example.com", CurrentPassword = "x", NewPassword = "y" };

        _dataLayer.FindByEmailAsync("a@example.com", Arg.Any<CancellationToken>()).Returns((ApplicationUser?)null);
        var unknown = await Assert.ThrowsAsync<DomainValidationException>(
            () => _business.ChangePasswordAsync(viewModel, CancellationToken.None));

        var user = new ApplicationUser { Email = "a@example.com" };
        _dataLayer.FindByEmailAsync("a@example.com", Arg.Any<CancellationToken>()).Returns(user);
        _dataLayer.ChangePasswordAsync(user, "x", "y", Arg.Any<CancellationToken>()).Returns(IdentityResult.Failed(
            new IdentityError { Code = nameof(IdentityErrorDescriber.PasswordMismatch), Description = "Incorrect." }));
        var mismatch = await Assert.ThrowsAsync<DomainValidationException>(
            () => _business.ChangePasswordAsync(viewModel, CancellationToken.None));

        Assert.Equal(unknown.Errors.Keys, mismatch.Errors.Keys);
        Assert.Equal(unknown.Errors["InvalidCredentials"], mismatch.Errors["InvalidCredentials"]);
    }

    [Fact]
    public async Task ChangePassword_PolicyFailure_ReportsTheIdentityErrors()
    {
        var user = new ApplicationUser { Email = "a@example.com" };
        _dataLayer.FindByEmailAsync("a@example.com", Arg.Any<CancellationToken>()).Returns(user);
        _dataLayer.ChangePasswordAsync(user, "x", "y", Arg.Any<CancellationToken>()).Returns(IdentityResult.Failed(
            new IdentityError { Code = "PasswordTooShort", Description = "Too short." }));

        var ex = await Assert.ThrowsAsync<DomainValidationException>(() => _business.ChangePasswordAsync(
            new ChangePasswordViewModel { Email = "a@example.com", CurrentPassword = "x", NewPassword = "y" },
            CancellationToken.None));

        Assert.Equal(["Too short."], ex.Errors["PasswordTooShort"]);
    }

    [Fact]
    public async Task ChangePassword_Success_DoesNotThrow()
    {
        var user = new ApplicationUser { Email = "a@example.com" };
        _dataLayer.FindByEmailAsync("a@example.com", Arg.Any<CancellationToken>()).Returns(user);
        _dataLayer.ChangePasswordAsync(user, "x", "y", Arg.Any<CancellationToken>()).Returns(IdentityResult.Success);

        await _business.ChangePasswordAsync(
            new ChangePasswordViewModel { Email = "a@example.com", CurrentPassword = "x", NewPassword = "y" },
            CancellationToken.None);

        await _dataLayer.Received(1).ChangePasswordAsync(user, "x", "y", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ValidateCredentials_UnknownEmail_ReturnsNull()
    {
        _dataLayer.FindByEmailAsync("a@example.com", Arg.Any<CancellationToken>()).Returns((ApplicationUser?)null);

        Assert.Null(await _business.ValidateCredentialsAsync(
            new SignInViewModel { Email = "a@example.com", Password = "x" }, CancellationToken.None));
    }

    [Fact]
    public async Task ValidateCredentials_WrongPassword_ReturnsNull_AndNeverReadsRoles()
    {
        var user = new ApplicationUser { Email = "a@example.com" };
        _dataLayer.FindByEmailAsync("a@example.com", Arg.Any<CancellationToken>()).Returns(user);
        _dataLayer.CheckPasswordSignInAsync(user, "x", Arg.Any<CancellationToken>()).Returns(false);

        Assert.Null(await _business.ValidateCredentialsAsync(
            new SignInViewModel { Email = "a@example.com", Password = "x" }, CancellationToken.None));
        await _dataLayer.DidNotReceiveWithAnyArgs().GetRolesAsync(default!, default);
    }

    [Fact]
    public async Task ValidateCredentials_Correct_ReturnsSubjectWithRoles()
    {
        var user = new ApplicationUser { Email = "a@example.com" };
        _dataLayer.FindByEmailAsync("a@example.com", Arg.Any<CancellationToken>()).Returns(user);
        _dataLayer.CheckPasswordSignInAsync(user, "ok", Arg.Any<CancellationToken>()).Returns(true);
        _dataLayer.GetRolesAsync(user, Arg.Any<CancellationToken>()).Returns(["PlatformAdmin"]);

        var subject = await _business.ValidateCredentialsAsync(
            new SignInViewModel { Email = "a@example.com", Password = "ok" }, CancellationToken.None);

        Assert.NotNull(subject);
        Assert.Equal(user.Id, subject!.UserId);
        Assert.Equal(["PlatformAdmin"], subject.Roles);
    }

    [Fact]
    public async Task GetSignInSubject_UnknownUser_ReturnsNull()
    {
        _dataLayer.FindByIdAsync("u1", Arg.Any<CancellationToken>()).Returns((ApplicationUser?)null);

        Assert.Null(await _business.GetSignInSubjectAsync("u1", CancellationToken.None));
    }

    [Fact]
    public async Task GetSignInSubject_UserCannotSignIn_ReturnsNull()
    {
        var user = new ApplicationUser();
        _dataLayer.FindByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _dataLayer.CanSignInAsync(user, Arg.Any<CancellationToken>()).Returns(false);

        Assert.Null(await _business.GetSignInSubjectAsync(user.Id, CancellationToken.None));
    }

    [Fact]
    public async Task GetSignInSubject_RereadsRolesFromTheStore()
    {
        var user = new ApplicationUser();
        _dataLayer.FindByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _dataLayer.CanSignInAsync(user, Arg.Any<CancellationToken>()).Returns(true);
        _dataLayer.GetRolesAsync(user, Arg.Any<CancellationToken>()).Returns([]);

        var subject = await _business.GetSignInSubjectAsync(user.Id, CancellationToken.None);

        Assert.NotNull(subject);
        Assert.Equal(user.Id, subject!.UserId);
        Assert.Empty(subject.Roles);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
