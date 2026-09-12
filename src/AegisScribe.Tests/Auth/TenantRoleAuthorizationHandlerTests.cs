using System.Security.Claims;
using AegisScribe.ApiService.Auth;
using AegisScribe.Domain.Context;
using AegisScribe.Domain.Facade;
using AegisScribe.Domain.Managers.Models.Domain;
using Microsoft.AspNetCore.Authorization;
using NSubstitute;

namespace AegisScribe.Tests.Auth;

// One handler backs all three tenant policies (auth.md); this exercises it directly against
// AuthorizationHandlerContext, the same isolation style as TenantBusinessTests, since no controller
// carries a tenant policy yet to test through the endpoint (2.7 hasn't landed).
public class TenantRoleAuthorizationHandlerTests
{
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();
    private readonly ITenantResolutionFacade _tenantResolution = Substitute.For<ITenantResolutionFacade>();
    private readonly TenantRoleAuthorizationHandler _handler;
    private readonly Guid _tenantId = Guid.NewGuid();

    public TenantRoleAuthorizationHandlerTests()
    {
        _handler = new TenantRoleAuthorizationHandler(_tenantContext, _tenantResolution);
        _tenantContext.TenantId.Returns(_tenantId);
    }

    private async Task<bool> SucceedsAsync(TenantRole? membershipRole, TenantRole minimumRole)
    {
        _tenantResolution.GetRoleForCurrentUserAsync(_tenantId, Arg.Any<CancellationToken>()).Returns(membershipRole);

        var requirement = new TenantRoleRequirement(minimumRole);
        var context = new AuthorizationHandlerContext([requirement], new ClaimsPrincipal(), resource: null);
        await _handler.HandleAsync(context);
        return context.HasSucceeded;
    }

    [Theory]
    [InlineData(TenantRole.Member, TenantRole.Member, true)]
    [InlineData(TenantRole.Member, TenantRole.Officer, false)]
    [InlineData(TenantRole.Member, TenantRole.Owner, false)]
    [InlineData(TenantRole.Officer, TenantRole.Member, true)]
    [InlineData(TenantRole.Officer, TenantRole.Officer, true)]
    [InlineData(TenantRole.Officer, TenantRole.Owner, false)]
    [InlineData(TenantRole.Owner, TenantRole.Member, true)]
    [InlineData(TenantRole.Owner, TenantRole.Officer, true)]
    [InlineData(TenantRole.Owner, TenantRole.Owner, true)]
    public async Task EachRank_AgainstEachPolicy_MatchesTheOrdering(
        TenantRole membershipRole, TenantRole policyMinimum, bool expectedToSucceed)
    {
        Assert.Equal(expectedToSucceed, await SucceedsAsync(membershipRole, policyMinimum));
    }

    [Fact]
    public async Task NoMembership_FailsEvenTheLowestPolicy()
    {
        Assert.False(await SucceedsAsync(null, TenantRole.Member));
    }

    [Fact]
    public async Task NoResolvedTenant_FailsClosed_WithoutQueryingMembership()
    {
        _tenantContext.TenantId.Returns(_ => throw new InvalidOperationException("No tenant has been resolved for this request."));

        var requirement = new TenantRoleRequirement(TenantRole.Member);
        var context = new AuthorizationHandlerContext([requirement], new ClaimsPrincipal(), resource: null);
        await _handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
        await _tenantResolution.DidNotReceive().GetRoleForCurrentUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
