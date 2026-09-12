using AegisScribe.Domain.Data;
using AegisScribe.Domain.Managers.Models.Identity;
using Microsoft.AspNetCore.Identity;
using NSubstitute;

namespace AegisScribe.Tests.Auth;

// DataLayer: pass-throughs today. These pin the delegation so the seam can't quietly start doing
// something else (the repository itself is exercised against real SQL by AuthEndpointTests).
public class UserDataLayerTests
{
    private readonly IUserRepository _repository = Substitute.For<IUserRepository>();
    private readonly UserDataLayer _dataLayer;
    private readonly ApplicationUser _user = new();

    public UserDataLayerTests()
    {
        _dataLayer = new UserDataLayer(_repository);
    }

    [Fact]
    public async Task Create_Delegates()
    {
        _repository.CreateAsync(_user, "pwd", Arg.Any<CancellationToken>()).Returns(IdentityResult.Success);

        Assert.Same(IdentityResult.Success, await _dataLayer.CreateAsync(_user, "pwd", CancellationToken.None));
    }

    [Fact]
    public async Task Finds_Delegate()
    {
        _repository.FindByEmailAsync("a@example.com", Arg.Any<CancellationToken>()).Returns(_user);
        _repository.FindByIdAsync(_user.Id, Arg.Any<CancellationToken>()).Returns(_user);

        Assert.Same(_user, await _dataLayer.FindByEmailAsync("a@example.com", CancellationToken.None));
        Assert.Same(_user, await _dataLayer.FindByIdAsync(_user.Id, CancellationToken.None));
    }

    [Fact]
    public async Task ChangePassword_Delegates()
    {
        _repository.ChangePasswordAsync(_user, "old", "new", Arg.Any<CancellationToken>()).Returns(IdentityResult.Success);

        Assert.Same(IdentityResult.Success, await _dataLayer.ChangePasswordAsync(_user, "old", "new", CancellationToken.None));
    }

    [Fact]
    public async Task SignInChecks_Delegate()
    {
        _repository.CheckPasswordSignInAsync(_user, "pwd", Arg.Any<CancellationToken>()).Returns(true);
        _repository.CanSignInAsync(_user, Arg.Any<CancellationToken>()).Returns(true);
        _repository.GetRolesAsync(_user, Arg.Any<CancellationToken>()).Returns(["PlatformAdmin"]);

        Assert.True(await _dataLayer.CheckPasswordSignInAsync(_user, "pwd", CancellationToken.None));
        Assert.True(await _dataLayer.CanSignInAsync(_user, CancellationToken.None));
        Assert.Equal(["PlatformAdmin"], await _dataLayer.GetRolesAsync(_user, CancellationToken.None));
    }
}
