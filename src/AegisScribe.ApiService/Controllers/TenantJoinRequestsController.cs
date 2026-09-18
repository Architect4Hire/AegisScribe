using AegisScribe.ApiService.Auth;
using AegisScribe.Domain.Facade;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AegisScribe.ApiService.Controllers;

// The officer's queue. Creating a request is the other door and lives on CommunitiesController,
// tenant-less, because the person asking is by definition not a member yet.
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/t/{tenantSlug}/join-requests")]
[Authorize(Policy = AuthPolicies.TenantOfficer)]
public class TenantJoinRequestsController(IMembershipFacade membershipFacade) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<JoinRequestServiceModel>>> List(
        [FromQuery] JoinRequestStatus? status, CancellationToken ct) =>
        Ok(await membershipFacade.ListJoinRequestsAsync(status, ct));

    // The role is in the body because approving is a role grant: an officer may admit somebody as a
    // Member or an Officer, and only an Owner may admit one straight in as an Owner. Same ceiling as
    // an invitation, enforced in the same place.
    [HttpPost("{requestId:guid}/approve")]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Approve(
        Guid requestId, SetMemberRoleViewModel viewModel, CancellationToken ct) =>
        await membershipFacade.ApproveJoinRequestAsync(requestId, viewModel, ct) ? NoContent() : NotFound();

    // 404 covers both "no such request here" and "somebody already decided it" — the second is a lost
    // race on a queue two officers are working, not an error either of them made.
    [HttpPost("{requestId:guid}/decline")]
    public async Task<IActionResult> Decline(Guid requestId, CancellationToken ct) =>
        await membershipFacade.DeclineJoinRequestAsync(requestId, ct) ? NoContent() : NotFound();
}
