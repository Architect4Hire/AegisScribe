using AegisScribe.Domain.Facade;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AegisScribe.ApiService.Controllers;

// auth.md: "the signed-in user's memberships come from GET /api/v1/me". Memberships arrive with
// tenant membership in Phase 2.7; for now this returns the Identity user only.
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/me")]
[Authorize]
public class MeController(IMeFacade meFacade) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<UserServiceModel>> Get(CancellationToken ct) =>
        Ok(await meFacade.GetAsync(ct));
}
