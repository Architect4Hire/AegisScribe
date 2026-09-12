using System.Security.Claims;
using AegisScribe.ApiService.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace AegisScribe.Tests.Auth;

// ICurrentUser is what Business trusts for "who is this" — so its contract is pinned here directly,
// in particular that a machine token is never mistaken for a user.
public class HttpCurrentUserTests
{
    private static HttpCurrentUser For(params Claim[] claims)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "test", nameType: "name", roleType: "role")),
        };
        var options = new IdentityOptions();
        options.ClaimsIdentity.UserIdClaimType = "sub"; // as Program.cs configures it
        return new HttpCurrentUser(new HttpContextAccessor { HttpContext = context }, Options.Create(options));
    }

    [Fact]
    public void HumanToken_UserIdIsTheSubject()
    {
        var user = For(new Claim("sub", "d9603d1e"), new Claim("client_id", "aegisscribe-bff"));

        Assert.Equal("d9603d1e", user.UserId);
        Assert.Equal("d9603d1e", user.Subject);
    }

    [Fact]
    public void ClientCredentialsToken_HasNoUserId_ButKeepsItsSubject()
    {
        var machine = For(new Claim("sub", "aegisscribe-ops"), new Claim("client_id", "aegisscribe-ops"));

        Assert.Null(machine.UserId);
        Assert.Equal("aegisscribe-ops", machine.Subject);
    }

    [Fact]
    public void Anonymous_HasNothing()
    {
        var anonymous = new HttpCurrentUser(
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() }, Options.Create(new IdentityOptions()));

        Assert.Null(anonymous.UserId);
        Assert.Null(anonymous.Subject);
        Assert.False(anonymous.IsPlatformAdmin);
    }

    [Fact]
    public void PlatformAdmin_ReadsTheRoleClaim()
    {
        var admin = For(new Claim("sub", "u1"), new Claim("role", AuthPolicies.PlatformAdmin));

        Assert.True(admin.IsPlatformAdmin);
    }
}
