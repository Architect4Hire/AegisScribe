using AegisScribe.ApiService.Infrastructure;
using AegisScribe.ApiService.Infrastructure.Idempotency;
using AegisScribe.Domain.Facade;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AegisScribe.ApiService.Controllers;

// Tenant-less: the community doesn't exist yet, so there's no /t/{slug} to resolve into — any
// authenticated user may create one, and they become its first Owner (TenantBusiness.CreateAsync).
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/tenants")]
[Authorize]
public class TenantsController(ITenantFacade tenantFacade) : ControllerBase
{
    // Tenant-less like the POST below it, and authenticated for the same reason the endpoint is
    // narrow: it answers "is this slug free" and nothing else. See SlugCheckReason — no answer names
    // the community holding a taken slug.
    [HttpGet("slug-check")]
    [EnableRateLimiting(RateLimiterPolicies.SlugCheck)]
    public async Task<ActionResult<SlugCheckServiceModel>> CheckSlug(
        [FromQuery] string? name, [FromQuery] string? slug, CancellationToken ct) =>
        Ok(await tenantFacade.CheckSlugAsync(new SlugCheckViewModel { Name = name, Slug = slug }, ct));

    [HttpPost]
    [Idempotent]
    [ProducesResponseType(StatusCodes.Status201Created)]
    // 409 is the slug race only — a slug already taken when the request arrives is still the 400 it
    // has always been (SlugTakenException).
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TenantServiceModel>> Create(CreateTenantViewModel viewModel, CancellationToken ct)
    {
        var tenant = await tenantFacade.CreateAsync(viewModel, ct);
        return CreatedAtAction(nameof(TenantController.Get), "Tenant", new { tenantSlug = tenant.Slug }, tenant);
    }
}
