using AegisScribe.ApiService.Auth;
using AegisScribe.Domain.Context;
using AegisScribe.Domain.Facade;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AegisScribe.ApiService.Controllers;

// Owner-only for both actions (auth.md: policies answer "what rank are you here", already fully
// answered by 2.6's TenantRoleAuthorizationHandler — no separate Business-side ownership check).
// tenantSlug is never bound into a ViewModel or passed down; the resolved ITenantContext.TenantId
// is the only tenant identifier that reaches the facade (add-endpoint skill).
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/t/{tenantSlug}")]
[Authorize(Policy = AuthPolicies.TenantOwner)]
public class TenantController(ITenantFacade tenantFacade, ITenantContext tenantContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<TenantServiceModel>> Get(CancellationToken ct) =>
        Ok(await tenantFacade.GetByIdAsync(tenantContext.TenantId, ct));

    [HttpPatch]
    public async Task<ActionResult<TenantServiceModel>> Rename(RenameTenantViewModel viewModel, CancellationToken ct) =>
        Ok(await tenantFacade.RenameAsync(tenantContext.TenantId, viewModel, ct));
}
