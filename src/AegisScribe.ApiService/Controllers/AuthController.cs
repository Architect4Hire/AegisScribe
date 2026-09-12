using AegisScribe.Domain.Facade;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AegisScribe.ApiService.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth")]
public class AuthController(IAuthFacade authFacade, IMeFacade meFacade) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<UserServiceModel>> Register(RegisterViewModel viewModel, CancellationToken ct) =>
        Ok(await authFacade.RegisterAsync(viewModel, ct));

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel viewModel, CancellationToken ct)
    {
        await authFacade.ChangePasswordAsync(viewModel, ct);
        return NoContent();
    }

    // Placeholder only: there is no session to end and no OpenIddict token yet to revoke.
    // Real sign-out happens at connect/revoke (auth.md's revoke-then-clear flow), called by
    // whichever client holds the refresh token — the gateway, for a browser session.
    [HttpPost("logout")]
    public IActionResult Logout() => NoContent();

    // Isolates "did the token authenticate" from business logic: /me does a real Identity
    // lookup by sub, so it 401s for a client-credentials token regardless of whether validation
    // succeeded (no Identity user is named e.g. "aegisscribe-ops"). This just echoes the claims
    // the resource server actually accepted.
    [HttpGet("whoami")]
    [Authorize]
    public ActionResult<WhoAmIServiceModel> WhoAmI() => Ok(meFacade.WhoAmI());
}
