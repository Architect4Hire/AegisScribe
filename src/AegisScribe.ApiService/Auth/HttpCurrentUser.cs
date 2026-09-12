using System.Security.Claims;
using AegisScribe.Domain.Context;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;

namespace AegisScribe.ApiService.Auth;

// The API's ICurrentUser: reads the principal the token validation handler produced. UserId uses
// Identity's configured user-id claim type (sub — see Program.cs) — the same lookup
// UserManager.GetUserId performs.
public sealed class HttpCurrentUser(IHttpContextAccessor httpContextAccessor, IOptions<IdentityOptions> identityOptions)
    : ICurrentUser
{
    // Null for a client-credentials token, per ICurrentUser's contract. Such a token's sub IS its
    // client id (AuthorizationController's client_credentials branch), so sub == client_id is what
    // marks a machine caller. Enforced here, explicitly, rather than left to the membership table's
    // FK to AspNetUsers happening to match no row — that is a schema accident, not a control.
    public string? UserId
    {
        get
        {
            var userId = User?.FindFirst(identityOptions.Value.ClaimsIdentity.UserIdClaimType)?.Value;
            var clientId = User?.FindFirst(OpenIddictConstants.Claims.ClientId)?.Value;
            return userId is not null && userId == clientId ? null : userId;
        }
    }

    public string? Subject => User?.FindFirst(OpenIddictConstants.Claims.Subject)?.Value;

    public bool IsPlatformAdmin => User?.IsInRole(AuthPolicies.PlatformAdmin) ?? false;

    private ClaimsPrincipal? User => httpContextAccessor.HttpContext?.User;
}
