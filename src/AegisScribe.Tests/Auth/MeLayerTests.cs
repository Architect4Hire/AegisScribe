using AegisScribe.Domain.Business;
using AegisScribe.Domain.Context;
using AegisScribe.Domain.Data;
using AegisScribe.Domain.Facade;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.Identity;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using NSubstitute;

namespace AegisScribe.Tests.Auth;

public class MeBusinessTests
{
    private readonly IUserDataLayer _dataLayer = Substitute.For<IUserDataLayer>();
    private readonly ITenantDataLayer _tenantDataLayer = Substitute.For<ITenantDataLayer>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly MeBusiness _business;

    public MeBusinessTests()
    {
        _business = new MeBusiness(_dataLayer, _tenantDataLayer, _currentUser);
    }

    [Fact]
    public async Task Get_NoUserId_RequiresAuthentication()
    {
        _currentUser.UserId.Returns((string?)null);

        await Assert.ThrowsAsync<AuthenticationRequiredException>(() => _business.GetAsync(CancellationToken.None));
        await _dataLayer.DidNotReceiveWithAnyArgs().FindByIdAsync(default!, default);
    }

    [Fact]
    public async Task Get_UserNoLongerExists_RequiresAuthentication()
    {
        _currentUser.UserId.Returns("u1");
        _dataLayer.FindByIdAsync("u1", Arg.Any<CancellationToken>()).Returns((ApplicationUser?)null);

        await Assert.ThrowsAsync<AuthenticationRequiredException>(() => _business.GetAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Get_MapsTheCallersOwnRecord()
    {
        var user = new ApplicationUser { Email = "a@example.com", DisplayName = "Ann", CreatedAt = DateTimeOffset.UnixEpoch };
        _currentUser.UserId.Returns(user.Id);
        _dataLayer.FindByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _tenantDataLayer.GetMembershipsForUserAsync(user.Id, Arg.Any<CancellationToken>()).Returns([]);

        var result = await _business.GetAsync(CancellationToken.None);

        Assert.Equal(user.Id, result.Id);
        Assert.Equal("a@example.com", result.Email);
        Assert.Equal("Ann", result.DisplayName);
        Assert.Equal(DateTimeOffset.UnixEpoch, result.CreatedAt);
    }

    [Fact]
    public async Task Get_IncludesTheCallersTenantMemberships()
    {
        var user = new ApplicationUser { Email = "a@example.com", CreatedAt = DateTimeOffset.UnixEpoch };
        var memberships = new List<TenantMembershipServiceModel>
        {
            new() { TenantId = Guid.NewGuid(), TenantSlug = "emberwatch", TenantName = "Emberwatch", Role = TenantRole.Officer, JoinedAt = DateTimeOffset.UnixEpoch },
        };
        _currentUser.UserId.Returns(user.Id);
        _dataLayer.FindByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _tenantDataLayer.GetMembershipsForUserAsync(user.Id, Arg.Any<CancellationToken>()).Returns(memberships);

        var result = await _business.GetAsync(CancellationToken.None);

        Assert.Same(memberships, result.Memberships);
    }

    [Fact]
    public void WhoAmI_EchoesTheAcceptedClaims_WithoutTouchingTheStore()
    {
        _currentUser.Subject.Returns("aegisscribe-ops");
        _currentUser.IsPlatformAdmin.Returns(false);

        var result = _business.WhoAmI();

        Assert.Equal("aegisscribe-ops", result.Sub);
        Assert.False(result.IsPlatformAdmin);
        Assert.Empty(_dataLayer.ReceivedCalls());
    }
}

public class MeFacadeTests
{
    [Fact]
    public async Task Delegates()
    {
        var business = Substitute.For<IMeBusiness>();
        var user = new UserServiceModel { Id = "u1" };
        var whoAmI = new WhoAmIServiceModel { Sub = "u1" };
        business.GetAsync(Arg.Any<CancellationToken>()).Returns(user);
        business.WhoAmI().Returns(whoAmI);
        var facade = new MeFacade(business);

        Assert.Same(user, await facade.GetAsync(CancellationToken.None));
        Assert.Same(whoAmI, facade.WhoAmI());
    }
}
