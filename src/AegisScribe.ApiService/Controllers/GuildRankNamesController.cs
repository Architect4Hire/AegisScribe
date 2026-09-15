using AegisScribe.ApiService.Auth;
using AegisScribe.Domain.Facade;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AegisScribe.ApiService.Controllers;

// What this community calls the in-game ranks of the guilds it follows (7.5).
//
// The table exists because Blizzard does not give us the names — the guild roster endpoint returns a
// rank NUMBER, 0-9, and nothing more. Somebody has to type "Veteran", and these are the routes where
// they do it.
//
// Not to be confused with /ranks, which is the community's OWN ladder (TenantRank). Three rank
// concepts, none derived from another; this one only ever labels a number the game reported.
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/t/{tenantSlug}/guild-rank-names")]
public class GuildRankNamesController(IGuildRankNameFacade rankNameFacade) : ControllerBase
{
    // TenantMember, because the roster shows these to everyone who can see the roster. Naming them is
    // the officers' job; reading them is not privileged.
    [HttpGet]
    [Authorize(Policy = AuthPolicies.TenantMember)]
    public async Task<ActionResult<IReadOnlyList<GuildRankNameServiceModel>>> List(CancellationToken ct) =>
        Ok(await rankNameFacade.ListAsync(ct));

    // The guild and the rank are route segments rather than body fields: they identify which rank is
    // being named, and a body that could name a guild would be a way to write a row about a guild this
    // community does not follow.
    [HttpPut("{guildId:guid}/{rank:int}")]
    [Authorize(Policy = AuthPolicies.TenantOfficer)]
    public async Task<IActionResult> Set(
        Guid guildId, int rank, SetGuildRankNameViewModel viewModel, CancellationToken ct)
    {
        var set = await rankNameFacade.SetAsync(guildId, rank, viewModel, ct);

        // False means this community does not follow that guild — a 404, and the same answer a guild
        // that does not exist would get, because the query filter makes the two indistinguishable.
        return set ? NoContent() : NotFound();
    }
}
