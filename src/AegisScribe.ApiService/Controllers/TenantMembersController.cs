using AegisScribe.ApiService.Auth;
using AegisScribe.Domain.Facade;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AegisScribe.ApiService.Controllers;

// The people in this community, and the two acts that change who they are.
//
// Both writes sit behind TenantOfficer, which answers "are you at least an officer here" and nothing
// more. WHICH members an officer may act on, what role they may grant, and the last-owner rule all
// need to read the actor's and the target's standing, so they are Business's — and they surface as
// 403s from the domain handler (auth.md).
//
// tenantSlug never binds into a ViewModel and never reaches the facade; ITenantContext.TenantId is the
// only tenant identifier below the controller. A caller with no membership gets a 404 from the
// resolution middleware, never a 403.
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/t/{tenantSlug}/members")]
public class TenantMembersController(IMembershipFacade membershipFacade) : ControllerBase
{
    // TenantMember: who is in your community is not privileged information inside it. Contact details
    // are, and never leave the database — the projection carries DisplayName only.
    [HttpGet]
    [Authorize(Policy = AuthPolicies.TenantMember)]
    public async Task<ActionResult<CursorPageServiceModel<TenantMemberServiceModel>>> List(
        [FromQuery] string? cursor,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        var (afterJoinedAt, afterId) = MemberCursor.Decode(cursor);
        var members = await membershipFacade.ListMembersAsync(afterJoinedAt, afterId, limit, ct);
        var hasMore = members.Count == limit;

        return Ok(new CursorPageServiceModel<TenantMemberServiceModel>
        {
            Items = members,
            NextCursor = hasMore ? MemberCursor.Encode(members[^1]) : null,
            HasMore = hasMore,
        });
    }

    // Leaving of your own accord. TenantMember, because every member may do it — including an Officer,
    // who outranks nobody able to remove them and would otherwise need an Owner's cooperation to walk
    // away. The last-owner rule is the only thing that can refuse it.
    //
    // Declared before the {userId} route and matched ahead of it regardless: a literal segment beats a
    // parameter one in ASP.NET Core's route precedence, so this cannot be shadowed by a user whose id
    // is the string "me".
    [HttpDelete("me")]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Policy = AuthPolicies.TenantMember)]
    public async Task<IActionResult> Leave(CancellationToken ct)
    {
        await membershipFacade.LeaveAsync(ct);

        return NoContent();
    }

    [HttpPut("{userId}/role")]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Policy = AuthPolicies.TenantOfficer)]
    public async Task<IActionResult> SetRole(
        string userId, SetMemberRoleViewModel viewModel, CancellationToken ct) =>
        await membershipFacade.SetRoleAsync(userId, viewModel, ct) ? NoContent() : NotFound();

    // Takes the member's claims and roster entries in THIS community with it; their global character
    // data is untouched (auth.md). Not idempotent-by-404: a member who is already gone is a satisfied
    // intent, but the last-owner refusal is a real 403 that must not read as success.
    [HttpDelete("{userId}")]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Policy = AuthPolicies.TenantOfficer)]
    public async Task<IActionResult> Remove(string userId, CancellationToken ct)
    {
        await membershipFacade.RemoveMemberAsync(userId, ct);

        // 204 whether or not they were there: DELETE is idempotent (api-contract.md), and "not a
        // member here" is indistinguishable from "already removed".
        return NoContent();
    }
}
