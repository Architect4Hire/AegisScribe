using AegisScribe.ApiService.Auth;
using AegisScribe.ApiService.Managers.Models.ViewModels;
using Asp.Versioning;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AegisScribe.ApiService.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth")]
public class AuthController(
    IAuthService authService,
    IValidator<RegisterViewModel> registerValidator,
    IValidator<ChangePasswordViewModel> changePasswordValidator) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterViewModel viewModel, CancellationToken ct)
    {
        var validation = await registerValidator.ValidateAsync(viewModel, ct);
        if (!validation.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        }

        var result = await authService.RegisterAsync(viewModel, ct);
        return result.Succeeded ? Ok(result.Value) : ValidationProblem(new ValidationProblemDetails(result.Errors));
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel viewModel, CancellationToken ct)
    {
        var validation = await changePasswordValidator.ValidateAsync(viewModel, ct);
        if (!validation.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        }

        var result = await authService.ChangePasswordAsync(viewModel, ct);
        return result.Succeeded ? NoContent() : ValidationProblem(new ValidationProblemDetails(result.Errors));
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
    public IActionResult WhoAmI() => Ok(new
    {
        sub = User.FindFirst("sub")?.Value,
        isPlatformAdmin = User.IsInRole(AuthPolicies.PlatformAdmin),
    });
}
