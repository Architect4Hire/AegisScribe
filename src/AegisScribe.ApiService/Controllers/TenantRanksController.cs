using AegisScribe.ApiService.Auth;
using AegisScribe.ApiService.Infrastructure.Idempotency;
using AegisScribe.Domain.Facade;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AegisScribe.ApiService.Controllers;

// A community's own rank ladder (7.1) — "Raider", "Trial", "Social". Not the in-game ranks Blizzard
// reports; those are GuildMember.BlizzardRank, global and read-only (tenancy.md).
//
// tenantSlug never binds into a ViewModel and never reaches the facade — ITenantContext.TenantId,
// resolved by the middleware, is the only tenant identifier below the controller. A caller with no
// membership in the named community gets a 404 from that middleware, never a 403, because a 403 would
// confirm the community exists.
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/t/{tenantSlug}/ranks")]
public class TenantRanksController(ITenantRankFacade rankFacade) : ControllerBase
{
    // Members read, officers write. The ladder is not privileged information inside the community —
    // the roster renders a rank pill against every character — but shaping it is an officer's job.
    [HttpGet]
    [Authorize(Policy = AuthPolicies.TenantMember)]
    public async Task<ActionResult<IReadOnlyList<TenantRankServiceModel>>> List(CancellationToken ct) =>
        Ok(await rankFacade.ListAsync(ct));

    // No Location header, because there is no GET /{rankId} route to point one at — no screen fetches
    // a single rank, and every route is a promise for the life of v1 (api-contract.md). Same 200-with-
    // the-created-row shape TenantGuildsController.Link returns.
    [HttpPost]
    [Idempotent]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [Authorize(Policy = AuthPolicies.TenantOfficer)]
    public async Task<ActionResult<TenantRankServiceModel>> Create(CreateRankViewModel viewModel, CancellationToken ct) =>
        Ok(await rankFacade.CreateAsync(viewModel, ct));

    // PUT, not PATCH: the body is the whole rank, which is what makes a retry over a flaky mobile
    // connection safe to repeat.
    [HttpPut("{rankId:guid}")]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [Authorize(Policy = AuthPolicies.TenantOfficer)]
    public async Task<ActionResult<TenantRankServiceModel>> Update(
        Guid rankId, UpdateRankViewModel viewModel, CancellationToken ct)
    {
        var rank = await rankFacade.UpdateAsync(rankId, viewModel, ct);

        // 404 covers both "no such rank" and "that rank is another community's" — they must be
        // indistinguishable, or the response becomes an existence oracle for rows the caller cannot
        // see (tenancy.md).
        return rank is null ? NotFound() : Ok(rank);
    }

    // 204 whether or not the rank was there: DELETE is idempotent and the client's intent is satisfied
    // either way (api-contract.md). Deliberately unlike TenantGuildsController.Unlink's 404 — nothing
    // crosses a tenant boundary here, because the query filter makes a delete of another community's
    // rank id match zero rows rather than theirs.
    //
    // The one case that is NOT 204 is a rank roster entries still hold (7.2): that is a 409, because
    // the alternative is silently un-ranking everyone who held it.
    [HttpDelete("{rankId:guid}")]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [Authorize(Policy = AuthPolicies.TenantOfficer)]
    public async Task<IActionResult> Delete(Guid rankId, CancellationToken ct)
    {
        await rankFacade.DeleteAsync(rankId, ct);

        return NoContent();
    }
}
