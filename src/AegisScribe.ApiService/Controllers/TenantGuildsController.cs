using AegisScribe.ApiService.Auth;
using AegisScribe.Domain.Context;
using AegisScribe.Domain.Facade;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AegisScribe.ApiService.Controllers;

// The guilds a community follows.
//
// tenantSlug never binds into a ViewModel and never reaches the facade — ITenantContext.TenantId is the
// only tenant identifier below the controller (tenancy.md).
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/t/{tenantSlug}/guilds")]
public class TenantGuildsController(IGuildFacade guildFacade, ITenantContext tenantContext) : ControllerBase
{
    // Members can see which guilds their community follows; only officers change or refresh them.
    [HttpGet]
    [Authorize(Policy = AuthPolicies.TenantMember)]
    public async Task<ActionResult<IReadOnlyList<GuildServiceModel>>> List(CancellationToken ct) =>
        Ok(await guildFacade.ListAsync(ct));

    // Linking IS the first sync: one Blizzard call fetches the guild and its whole roster together, so
    // a link that succeeded always has something behind it. A guild Blizzard does not know is a 404 and
    // nothing is linked — a typo leaves no dead link to clean up.
    [HttpPost]
    [Authorize(Policy = AuthPolicies.TenantOfficer)]
    public async Task<ActionResult<GuildServiceModel>> Link(LinkGuildViewModel viewModel, CancellationToken ct)
    {
        var guild = await guildFacade.LinkAsync(tenantContext.TenantId, viewModel, ct);

        return guild is null ? NotFound() : Ok(guild);
    }

    [HttpPost("{guildId:guid}/sync")]
    [Authorize(Policy = AuthPolicies.TenantOfficer)]
    public async Task<ActionResult<GuildServiceModel>> Resync(Guid guildId, CancellationToken ct)
    {
        var guild = await guildFacade.ResyncAsync(tenantContext.TenantId, guildId, ct);

        return guild is null ? NotFound() : Ok(guild);
    }

    // Removes this community's link only. The Guild and its members are global and stay — another
    // community may still follow it.
    [HttpDelete("{guildId:guid}")]
    [Authorize(Policy = AuthPolicies.TenantOfficer)]
    public async Task<IActionResult> Unlink(Guid guildId, CancellationToken ct) =>
        await guildFacade.UnlinkAsync(guildId, ct) ? NoContent() : NotFound();
}
