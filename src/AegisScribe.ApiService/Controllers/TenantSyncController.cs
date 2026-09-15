using AegisScribe.ApiService.Auth;
using AegisScribe.Domain.Context;
using AegisScribe.Domain.Facade;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AegisScribe.ApiService.Controllers;

// Tenant-triggered sync, and the budget that bounds it.
//
// tenantSlug is never bound into a ViewModel or passed down — the resolved ITenantContext.TenantId is
// the only tenant identifier that reaches the facade (tenancy.md: "never accept a TenantId from the
// client"). The slug in the route is for resolution middleware, not for the domain.
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/t/{tenantSlug}/sync")]
public class TenantSyncController(ISyncFacade syncFacade, ITenantContext tenantContext) : ControllerBase
{
    // Owner-only. external.md requires the budget be visible to the tenant's owner — a limit nobody can
    // see reads as a bug the first time it bites — but it is a fact about the community's spending, not
    // something every member needs.
    [HttpGet("budget")]
    [Authorize(Policy = AuthPolicies.TenantOwner)]
    public async Task<ActionResult<SyncBudgetServiceModel>> GetBudget(CancellationToken ct) =>
        Ok(await syncFacade.GetBudgetAsync(tenantContext.TenantId, ct));

    // Officer-level, not Owner: refreshing a character is ordinary roster upkeep, and the budget is
    // what stops it being abused rather than the policy.
    //
    // A 404 from Blizzard comes back as 404 here. The budget is still spent — the calls were made, and
    // charging only for successful answers would make "look up nonsense repeatedly" free.
    [HttpPost("characters/{realm}/{name}")]
    [Authorize(Policy = AuthPolicies.TenantOfficer)]
    public async Task<ActionResult<CharacterDetailServiceModel>> RefreshCharacter(
        string realm,
        string name,
        [FromQuery] string? region,
        CancellationToken ct)
    {
        var viewModel = new CharacterLookupViewModel
        {
            Region = region ?? "us",
            RealmSlug = realm,
            Name = name,
        };

        var character = await syncFacade.RefreshCharacterAsync(tenantContext.TenantId, viewModel, ct);

        return character is null ? NotFound() : Ok(character);
    }
}
