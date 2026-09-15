using AegisScribe.ApiService.Auth;
using AegisScribe.ApiService.Infrastructure.Idempotency;
using AegisScribe.Domain.Facade;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AegisScribe.ApiService.Controllers;

// "That character is mine" (7.2b) — a claim ties an Identity user to a character, inside one community.
//
// Claiming is NOT Battle.net verification (out of scope, CLAUDE.md) and asserts nothing to Blizzard.
// It grants no permission either: rank and OfficerNote stay TenantOfficer-gated regardless of who
// claims what. Nor does it require the character to be on this community's roster — adding someone to
// the roster and claiming a character are different acts by different people.
//
// tenantSlug never binds into a ViewModel and never reaches the facade.
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/t/{tenantSlug}/claims")]
public class CharacterClaimsController(ICharacterClaimFacade claimFacade) : ControllerBase
{
    // Claim state for one character. Always 200: an unclaimed character is a real answer with null
    // fields, not a 404 — a 404 would leave the client unable to tell "nobody has claimed this" from
    // "no such character".
    [HttpGet("{characterId:guid}")]
    [Authorize(Policy = AuthPolicies.TenantMember)]
    public async Task<ActionResult<CharacterClaimServiceModel>> Get(Guid characterId, CancellationToken ct) =>
        Ok(await claimFacade.GetAsync(characterId, ct));

    // Claims FOR THE CALLER. There is no field in the body naming a user and there will not be one —
    // the claimant comes from the validated token.
    [HttpPost]
    [Idempotent]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [Authorize(Policy = AuthPolicies.TenantMember)]
    public async Task<ActionResult<CharacterClaimServiceModel>> Claim(
        ClaimCharacterViewModel viewModel, CancellationToken ct)
    {
        var claim = await claimFacade.ClaimAsync(viewModel, ct);

        // Null means no such character — a 404 the foreign key would otherwise have made a 500.
        // Already-claimed-by-someone-else is a 409 from the exception handler; already claimed BY YOU
        // returns 200 with the existing claim, so a retried request is not a conflict.
        return claim is null ? NotFound() : Ok(claim);
    }

    // Releases the caller's OWN claim. Releasing someone else's is a 403 from Business — a rule, not a
    // policy, because answering it means reading the row first (auth.md).
    [HttpDelete("{characterId:guid}")]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Policy = AuthPolicies.TenantMember)]
    public async Task<IActionResult> Release(Guid characterId, CancellationToken ct)
    {
        await claimFacade.ReleaseAsync(characterId, ct);

        // 204 whether or not there was a claim: DELETE is idempotent and the intent is satisfied
        // either way (api-contract.md).
        return NoContent();
    }

    // An officer frees whoever holds the claim, and the act is audited. A separate route from the
    // self-release above because it is a separate act by a different actor on someone else's data —
    // and it FREES the claim, it never hands it to anyone.
    [HttpDelete("{characterId:guid}/holder")]
    [Authorize(Policy = AuthPolicies.TenantOfficer)]
    public async Task<IActionResult> ClearHolder(Guid characterId, CancellationToken ct)
    {
        await claimFacade.ClearAsync(characterId, ct);

        // 204 with no audit row when there was nothing to clear — an audit table that recorded things
        // that did not happen would be worse than one with gaps.
        return NoContent();
    }
}
