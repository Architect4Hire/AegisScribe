using AegisScribe.ApiService.Infrastructure.Idempotency;
using AegisScribe.Domain.Facade;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AegisScribe.ApiService.Controllers;

// Tenant-less: the community doesn't exist yet, so there's no /t/{slug} to resolve into — any
// authenticated user may create one, and they become its first Owner (TenantBusiness.CreateAsync).
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/tenants")]
[Authorize]
public class TenantsController(ITenantFacade tenantFacade) : ControllerBase
{
    [HttpPost]
    [Idempotent]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<ActionResult<TenantServiceModel>> Create(CreateTenantViewModel viewModel, CancellationToken ct)
    {
        var tenant = await tenantFacade.CreateAsync(viewModel, ct);
        return CreatedAtAction(nameof(TenantController.Get), "Tenant", new { tenantSlug = tenant.Slug }, tenant);
    }
}
