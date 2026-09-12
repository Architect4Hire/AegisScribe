using AegisScribe.Domain.Business;
using AegisScribe.Domain.Context;
using AegisScribe.Domain.Data;
using AegisScribe.Domain.Facade;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;
using NSubstitute;

namespace AegisScribe.Tests.Tenancy;

// The "unknown tenant and non-member are indistinguishable" rule lives in Business, so it is tested
// here in isolation as well as through the endpoint (TenantResolutionTests).
public class TenantBusinessTests
{
    private static readonly Tenant Tenant = new() { Id = Guid.NewGuid(), Slug = "emberwatch", Name = "Emberwatch", TimeZoneId = "UTC" };
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly ITenantDataLayer _dataLayer = Substitute.For<ITenantDataLayer>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly TenantBusiness _business;

    public TenantBusinessTests()
    {
        _business = new TenantBusiness(_dataLayer, _currentUser, new FixedTimeProvider(Now));
        _currentUser.UserId.Returns("u1");
    }

    [Fact]
    public async Task Member_ResolvesToTheTenant()
    {
        _dataLayer.FindBySlugAsync("emberwatch", Arg.Any<CancellationToken>()).Returns(Tenant);
        _dataLayer.IsMemberAsync(Tenant.Id, "u1", Arg.Any<CancellationToken>()).Returns(true);

        Assert.Equal(Tenant.Id, await _business.ResolveForCurrentUserAsync("emberwatch", CancellationToken.None));
    }

    [Fact]
    public async Task UnknownSlug_AndNonMember_GiveTheSameAnswer()
    {
        _dataLayer.FindBySlugAsync("nowhere", Arg.Any<CancellationToken>()).Returns((Tenant?)null);
        var unknown = await _business.ResolveForCurrentUserAsync("nowhere", CancellationToken.None);

        _dataLayer.FindBySlugAsync("emberwatch", Arg.Any<CancellationToken>()).Returns(Tenant);
        _dataLayer.IsMemberAsync(Tenant.Id, "u1", Arg.Any<CancellationToken>()).Returns(false);
        var nonMember = await _business.ResolveForCurrentUserAsync("emberwatch", CancellationToken.None);

        Assert.Null(unknown);
        Assert.Null(nonMember);
    }

    [Fact]
    public async Task NoIdentityUser_NeverResolves_AndNeverQueries()
    {
        // A client-credentials token authenticates but names no Identity user — it is a member of nothing.
        _currentUser.UserId.Returns((string?)null);

        Assert.Null(await _business.ResolveForCurrentUserAsync("emberwatch", CancellationToken.None));
        Assert.Empty(_dataLayer.ReceivedCalls());
    }

    [Fact]
    public async Task GetRoleForCurrentUser_Member_ReturnsTheirRole()
    {
        _dataLayer.GetRoleAsync(Tenant.Id, "u1", Arg.Any<CancellationToken>()).Returns(TenantRole.Officer);

        Assert.Equal(TenantRole.Officer, await _business.GetRoleForCurrentUserAsync(Tenant.Id, CancellationToken.None));
    }

    [Fact]
    public async Task GetRoleForCurrentUser_NonMember_ReturnsNull()
    {
        _dataLayer.GetRoleAsync(Tenant.Id, "u1", Arg.Any<CancellationToken>()).Returns((TenantRole?)null);

        Assert.Null(await _business.GetRoleForCurrentUserAsync(Tenant.Id, CancellationToken.None));
    }

    [Fact]
    public async Task GetRoleForCurrentUser_NoIdentityUser_NeverQueries()
    {
        _currentUser.UserId.Returns((string?)null);

        Assert.Null(await _business.GetRoleForCurrentUserAsync(Tenant.Id, CancellationToken.None));
        Assert.Empty(_dataLayer.ReceivedCalls());
    }

    [Fact]
    public async Task Create_SlugAlreadyTaken_ThrowsDomainValidation()
    {
        var viewModel = new CreateTenantViewModel { Slug = "emberwatch", Name = "Emberwatch", TimeZoneId = "UTC" };
        _dataLayer.FindBySlugAsync("emberwatch", Arg.Any<CancellationToken>()).Returns(Tenant);

        var ex = await Assert.ThrowsAsync<DomainValidationException>(() => _business.CreateAsync(viewModel, CancellationToken.None));
        Assert.Contains("Slug", ex.Errors.Keys);
        await _dataLayer.DidNotReceiveWithAnyArgs().CreateAsync(default!, default!, default);
    }

    [Fact]
    public async Task Create_NewSlug_CreatesTheTenantWithTheCallerAsOwner()
    {
        var viewModel = new CreateTenantViewModel { Slug = "newguild", Name = "New Guild", TimeZoneId = "UTC" };
        _dataLayer.FindBySlugAsync("newguild", Arg.Any<CancellationToken>()).Returns((Tenant?)null);
        _dataLayer.CreateAsync(Arg.Any<Tenant>(), Arg.Any<TenantMembership>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<Tenant>());

        var result = await _business.CreateAsync(viewModel, CancellationToken.None);

        Assert.Equal("newguild", result.Slug);
        Assert.Equal("New Guild", result.Name);
        await _dataLayer.Received(1).CreateAsync(
            Arg.Is<Tenant>(t => t.Slug == "newguild"),
            Arg.Is<TenantMembership>(m => m.UserId == "u1" && m.Role == TenantRole.Owner && m.JoinedAt == Now),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_NoIdentityUser_RequiresAuthentication()
    {
        _currentUser.UserId.Returns((string?)null);
        var viewModel = new CreateTenantViewModel { Slug = "newguild", Name = "New Guild", TimeZoneId = "UTC" };

        await Assert.ThrowsAsync<AuthenticationRequiredException>(() => _business.CreateAsync(viewModel, CancellationToken.None));
    }

    [Fact]
    public async Task GetById_MapsTheTenant()
    {
        _dataLayer.FindByIdAsync(Tenant.Id, Arg.Any<CancellationToken>()).Returns(Tenant);

        var result = await _business.GetByIdAsync(Tenant.Id, CancellationToken.None);

        Assert.Equal(Tenant.Id, result.Id);
        Assert.Equal(Tenant.Slug, result.Slug);
    }

    [Fact]
    public async Task Rename_UpdatesTheNameAndPersists()
    {
        var tenant = new Tenant { Id = Tenant.Id, Slug = Tenant.Slug, Name = Tenant.Name, TimeZoneId = Tenant.TimeZoneId };
        _dataLayer.FindByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        _dataLayer.RenameAsync(Arg.Any<Tenant>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<Tenant>());

        var result = await _business.RenameAsync(tenant.Id, new RenameTenantViewModel { Name = "Renamed" }, CancellationToken.None);

        Assert.Equal("Renamed", result.Name);
        await _dataLayer.Received(1).RenameAsync(Arg.Is<Tenant>(t => t.Name == "Renamed"), Arg.Any<CancellationToken>());
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

public class TenantResolutionFacadeTests
{
    [Fact]
    public async Task Delegates()
    {
        var business = Substitute.For<ITenantBusiness>();
        var tenantId = Guid.NewGuid();
        business.ResolveForCurrentUserAsync("emberwatch", Arg.Any<CancellationToken>()).Returns(tenantId);

        Assert.Equal(tenantId, await new TenantResolutionFacade(business).ResolveForCurrentUserAsync("emberwatch", CancellationToken.None));
    }

    [Fact]
    public async Task DelegatesGetRoleForCurrentUser()
    {
        var business = Substitute.For<ITenantBusiness>();
        var tenantId = Guid.NewGuid();
        business.GetRoleForCurrentUserAsync(tenantId, Arg.Any<CancellationToken>()).Returns(TenantRole.Owner);

        Assert.Equal(TenantRole.Owner, await new TenantResolutionFacade(business).GetRoleForCurrentUserAsync(tenantId, CancellationToken.None));
    }
}

public class TenantDataLayerTests
{
    [Fact]
    public async Task Delegates()
    {
        var repository = Substitute.For<ITenantRepository>();
        var tenant = new Tenant { Id = Guid.NewGuid(), Slug = "emberwatch", Name = "Emberwatch", TimeZoneId = "UTC" };
        repository.FindBySlugAsync("emberwatch", Arg.Any<CancellationToken>()).Returns(tenant);
        repository.IsMemberAsync(tenant.Id, "u1", Arg.Any<CancellationToken>()).Returns(true);
        repository.GetRoleAsync(tenant.Id, "u1", Arg.Any<CancellationToken>()).Returns(TenantRole.Member);
        repository.FindByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        var memberships = new List<TenantMembershipServiceModel>
        {
            new() { TenantId = tenant.Id, TenantSlug = tenant.Slug, TenantName = tenant.Name, Role = TenantRole.Member, JoinedAt = DateTimeOffset.UnixEpoch },
        };
        repository.GetMembershipsForUserAsync("u1", Arg.Any<CancellationToken>()).Returns(memberships);
        var dataLayer = new TenantDataLayer(repository);

        Assert.Same(tenant, await dataLayer.FindBySlugAsync("emberwatch", CancellationToken.None));
        Assert.True(await dataLayer.IsMemberAsync(tenant.Id, "u1", CancellationToken.None));
        Assert.Equal(TenantRole.Member, await dataLayer.GetRoleAsync(tenant.Id, "u1", CancellationToken.None));
        Assert.Same(tenant, await dataLayer.FindByIdAsync(tenant.Id, CancellationToken.None));
        Assert.Same(memberships, await dataLayer.GetMembershipsForUserAsync("u1", CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_ComposesTheTransaction_AddingTenantThenMembership()
    {
        var repository = Substitute.For<ITenantRepository>();
        repository.ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task<Tenant>>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<Func<CancellationToken, Task<Tenant>>>()(CancellationToken.None));
        var tenant = new Tenant { Id = Guid.NewGuid(), Slug = "newguild", Name = "New Guild", TimeZoneId = "UTC" };
        var membership = new TenantMembership { TenantId = tenant.Id, UserId = "u1", Role = TenantRole.Owner, JoinedAt = DateTimeOffset.UnixEpoch };
        var dataLayer = new TenantDataLayer(repository);

        var result = await dataLayer.CreateAsync(tenant, membership, CancellationToken.None);

        Assert.Same(tenant, result);
        Received.InOrder(() =>
        {
            repository.AddAsync(tenant, Arg.Any<CancellationToken>());
            repository.AddMembershipAsync(membership, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task RenameAsync_PersistsThroughTheRepository()
    {
        var repository = Substitute.For<ITenantRepository>();
        var tenant = new Tenant { Id = Guid.NewGuid(), Slug = "emberwatch", Name = "Renamed", TimeZoneId = "UTC" };
        var dataLayer = new TenantDataLayer(repository);

        var result = await dataLayer.RenameAsync(tenant, CancellationToken.None);

        Assert.Same(tenant, result);
        await repository.Received(1).UpdateNameAsync(tenant, Arg.Any<CancellationToken>());
    }
}
