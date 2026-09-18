using AegisScribe.ApiService.Auth;
using AegisScribe.Domain.Context;
using AegisScribe.Domain.Facade;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AegisScribe.ApiService.Controllers;

// What a community HAS — the one read behind the first-run checklist.
//
// Its own controller rather than an action on TenantController, and not for tidiness: that class
// carries [Authorize(Policy = TenantOwner)] at the class level, and an action-level policy in ASP.NET
// Core ADDS to the controller's rather than replacing it. An Officer would be refused by the class
// attribute no matter what the action said, and this checklist is Owner OR Officer.
//
// TenantOfficer rather than TenantMember because the answer is a setup to-do list: a plain member has
// nothing to do with it and gets the ordinary per-screen empty states instead.
//
// tenantSlug never binds into a ViewModel and never reaches the facade — ITenantContext.TenantId is
// the only tenant identifier below the controller (tenancy.md).
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/t/{tenantSlug}/overview")]
[Authorize(Policy = AuthPolicies.TenantOfficer)]
public class TenantOverviewController(
    ITenantOverviewFacade overviewFacade, ITenantContext tenantContext) : ControllerBase
{
    [HttpGet]
    // The 200 is declared explicitly because declaring the 403 suppresses the one ASP.NET would
    // otherwise infer from ActionResult<T> — and the mobile client is generated from the committed
    // document (backend.md), so an undeclared success shape is a client that cannot read the answer.
    // Same trap TenantInvitationsController.Create already documents; leaving it out here produced a
    // contract diff with a 403 and no 200 at all.
    [ProducesResponseType<TenantOverviewServiceModel>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<TenantOverviewServiceModel>> Get(CancellationToken ct) =>
        Ok(await overviewFacade.GetAsync(tenantContext.TenantId, ct));
}
