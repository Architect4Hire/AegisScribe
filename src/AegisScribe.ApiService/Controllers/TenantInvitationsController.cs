using AegisScribe.ApiService.Auth;
using AegisScribe.ApiService.Infrastructure.Idempotency;
using AegisScribe.Domain.Facade;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AegisScribe.ApiService.Controllers;

// The officer's half of invitations. The invitee's half — the preview and the accept — is 8.3b, and
// it is tenant-less on purpose: the person accepting is not a member yet, so there is no /t/{slug} for
// them to resolve into and the token is the only thing that names the community.
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/t/{tenantSlug}/invitations")]
[Authorize(Policy = AuthPolicies.TenantOfficer)]
public class TenantInvitationsController(IMembershipFacade membershipFacade) : ControllerBase
{
    // No token on any row here, ever. The plaintext is returned once, by Create; a list that handed
    // tokens back would turn officer-level read access into the ability to join as anybody.
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<InvitationServiceModel>>> List(CancellationToken ct) =>
        Ok(await membershipFacade.ListInvitationsAsync(ct));

    // Idempotency-Key'd because a mobile network can fail after the server commits, and a retry would
    // otherwise mint a SECOND live invitation — a spare key to the community that nobody knows exists.
    [HttpPost]
    [Idempotent]
    // The 200 is declared explicitly because declaring the 403 suppresses the one ASP.NET would
    // otherwise infer from ActionResult<T> — and the mobile client is generated from the committed
    // document (backend.md), so an undeclared success shape is a client that cannot read the token.
    [ProducesResponseType<CreatedInvitationServiceModel>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CreatedInvitationServiceModel>> Create(
        CreateInvitationViewModel viewModel, CancellationToken ct) =>
        Ok(await membershipFacade.CreateInvitationAsync(viewModel, ct));

    [HttpDelete("{invitationId:guid}")]
    public async Task<IActionResult> Revoke(Guid invitationId, CancellationToken ct)
    {
        await membershipFacade.RevokeInvitationAsync(invitationId, ct);

        // 204 whether or not there was something to revoke: DELETE is idempotent, and an invitation
        // that was already accepted, already revoked, or belongs to another community are all the same
        // answer to "make sure this link is dead" (api-contract.md).
        return NoContent();
    }
}
