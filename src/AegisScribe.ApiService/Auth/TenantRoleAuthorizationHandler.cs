using AegisScribe.Domain.Context;
using AegisScribe.Domain.Facade;
using Microsoft.AspNetCore.Authorization;

namespace AegisScribe.ApiService.Auth;

// auth.md: policies answer "what rank are you here" — this is the one handler behind all three
// (TenantMember/TenantOfficer/TenantOwner). The membership lookup goes through the tenant facade
// (2.3's ITenantResolutionFacade), never the DbContext directly.
public class TenantRoleAuthorizationHandler(ITenantContext tenantContext, ITenantResolutionFacade tenantResolution)
    : AuthorizationHandler<TenantRoleRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, TenantRoleRequirement requirement)
    {
        Guid tenantId;
        try
        {
            tenantId = tenantContext.TenantId;
        }
        catch (InvalidOperationException)
        {
            // No tenant resolved for this request — fail closed. Never substitute a default tenant
            // or a default Member role; an unsucceeded requirement already denies the request.
            return;
        }

        var ct = (context.Resource as HttpContext)?.RequestAborted ?? CancellationToken.None;
        var role = await tenantResolution.GetRoleForCurrentUserAsync(tenantId, ct);

        // TenantRole is ordered (Member=0 < Officer=10 < Owner=20), so this one comparison is the
        // whole three-policy story: no membership, or a role below the minimum, leaves the
        // requirement unsucceeded rather than granting anything.
        if (role is not null && role.Value >= requirement.MinimumRole)
        {
            context.Succeed(requirement);
        }
    }
}
