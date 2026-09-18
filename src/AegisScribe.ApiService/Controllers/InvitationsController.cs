using AegisScribe.ApiService.Infrastructure;
using AegisScribe.Domain.Facade;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AegisScribe.ApiService.Controllers;

// The invitee's half of the membership lifecycle, and the second route in this API that cannot live
// under /t/{tenantSlug}.
//
// The person here is not a member of anything yet, so no tenant was resolved for this request and
// there is no slug to resolve into. The TOKEN is the only thing that names a community — nothing here
// accepts a tenant id or slug beside it, because a token for community A accompanied by a slug for
// community B is the tenancy rule wearing a friendly hat (tenancy.md).
//
// BOTH actions are POSTs carrying the token in the body, including the one that is really a read.
// ASP.NET Core opens a logging scope carrying RequestPath on every request, so a token in the path
// lands on every log entry the request writes and on OpenTelemetry's url.path besides — the whole log
// pipeline, not just an access log. InvitationTokenSecrecyTests is what holds this shape in place.
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/invitations")]
public class InvitationsController(IMembershipFacade membershipFacade) : ControllerBase
{
    // Anonymous, deliberately. Somebody arriving on a link should see WHAT they were invited to before
    // deciding whether to sign in — requiring a session first would mean signing in to find out, and
    // would send the token through the OAuth round-trip before they knew it was worth anything.
    //
    // That makes it an unauthenticated lookup keyed by a secret, so it gets the tightest bucket: the
    // token is 256 bits and unguessable, but this is exactly the shape that earns one anyway.
    [HttpPost("preview")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiterPolicies.InvitationToken)]
    [ProducesResponseType<InvitationPreviewServiceModel>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    public async Task<ActionResult<InvitationPreviewServiceModel>> Preview(
        InvitationTokenViewModel viewModel, CancellationToken ct) =>
        Ok(await membershipFacade.PreviewInvitationAsync(viewModel.Token, ct));

    // Authorized: you must be somebody before you can be a member. Not Idempotency-Key'd, and it does
    // not need to be — the invitation itself is single-use, so a retried accept is refused by the same
    // guard that refuses a forwarded link.
    [HttpPost("accept")]
    [Authorize]
    [EnableRateLimiting(RateLimiterPolicies.InvitationToken)]
    [ProducesResponseType<InvitationAcceptedServiceModel>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    public async Task<ActionResult<InvitationAcceptedServiceModel>> Accept(
        InvitationTokenViewModel viewModel, CancellationToken ct) =>
        Ok(await membershipFacade.AcceptInvitationAsync(viewModel.Token, ct));
}
