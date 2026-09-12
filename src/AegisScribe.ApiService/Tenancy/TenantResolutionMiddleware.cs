using AegisScribe.Domain.Context;
using AegisScribe.Domain.Facade;

namespace AegisScribe.ApiService.Tenancy;

// The only place in the app allowed to call TenantContext.SetTenant — everything else depends on
// the read-only ITenantContext. The tenant comes ONLY from the route's {tenantSlug} segment, never a
// header, query string, body, or ApplicationUser.LastTenantId (tenancy.md). The lookup and the
// membership rule go through the facade stack; this class only decides what HTTP does with the answer.
public class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        ITenantResolutionFacade tenantResolution,
        TenantContext tenantContext)
    {
        var slug = context.GetRouteValue("tenantSlug") as string;
        if (slug is null)
        {
            // Tenant-less route (auth, me, platform, characters, ...) — nothing to resolve.
            await next(context);
            return;
        }

        if (context.User.Identity?.IsAuthenticated != true)
        {
            // No identity to check membership against. Defer to [Authorize]/UseAuthorization
            // downstream, which produces the normal bare 401 — this keeps the anonymous response
            // identical regardless of whether the named tenant exists.
            await next(context);
            return;
        }

        var tenantId = await tenantResolution.ResolveForCurrentUserAsync(slug, context.RequestAborted);
        if (tenantId is null)
        {
            // Unknown tenant and "tenant exists but you're not a member" arrive here identically —
            // a 403 would confirm the tenant exists (tenancy.md).
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        tenantContext.SetTenant(tenantId.Value);
        await next(context);
    }
}
