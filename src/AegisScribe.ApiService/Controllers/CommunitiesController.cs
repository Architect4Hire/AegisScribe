using AegisScribe.Domain.Facade;
using AegisScribe.Domain.Managers.Models.ViewModels;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AegisScribe.ApiService.Controllers;

// The one membership route that cannot live under /t/{tenantSlug}.
//
// TenantResolutionMiddleware resolves a slug only for a caller who is already a member — that check IS
// the resolution — so a stranger asking to join has no tenant to resolve into. The slug in this route
// is therefore the only thing naming a community, exactly as the token is for 8.3b's accept, and the
// id it resolves to is looked up server-side and never accepted from the client (tenancy.md).
//
// [Authorize] and no tenant policy: you must be signed in to ask, and there is no rank to hold yet.
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/communities")]
[Authorize]
public class CommunitiesController(IMembershipFacade membershipFacade) : ControllerBase
{
    [HttpPost("{slug}/join-requests")]
    public async Task<IActionResult> RequestToJoin(
        string slug, CreateJoinRequestViewModel viewModel, CancellationToken ct)
    {
        var accepted = await membershipFacade.RequestToJoinAsync(slug, viewModel, ct);

        // One 404 for two conditions, deliberately: no such community, and a community whose door is
        // shut. Telling those apart would make this route a directory of every community on the
        // platform — the same enumeration oracle the slug-availability endpoint refuses to be.
        return accepted ? NoContent() : NotFound();
    }
}
