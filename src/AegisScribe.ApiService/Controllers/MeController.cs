using AegisScribe.ApiService.Managers.Models.Identity;
using AegisScribe.ApiService.Managers.Models.ServiceModels;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AegisScribe.ApiService.Controllers;

// auth.md: "the signed-in user's memberships come from GET /api/v1/me". Memberships arrive with
// tenant membership in Phase 2; for now this returns the Identity user only, and exists as the
// first endpoint that actually requires a caller to be authenticated.
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/me")]
[Authorize]
public class MeController(UserManager<ApplicationUser> userManager) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var userId = userManager.GetUserId(User);
        if (userId is null)
        {
            return Unauthorized();
        }

        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return Unauthorized();
        }

        return Ok(new UserServiceModel
        {
            Id = user.Id,
            Email = user.Email!,
            DisplayName = user.DisplayName,
            CreatedAt = user.CreatedAt,
        });
    }
}
