using System.Security.Claims;
using AegisScribe.ApiService.Managers.Models.Identity;
using AegisScribe.ApiService.Managers.Models.ServiceModels;
using AegisScribe.ApiService.Managers.Models.ViewModels;
using FluentValidation;
using Microsoft.AspNetCore.Identity;

namespace AegisScribe.ApiService.Auth;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Auth");

        group.MapPost("/register", RegisterAsync);
        group.MapPost("/change-password", ChangePasswordAsync);

        // Placeholder only: there is no session to end and no OpenIddict token yet to revoke.
        // Real sign-out arrives in 1B.4 as OpenIddict's connect/revoke (auth.md's revoke-then-clear flow).
        group.MapPost("/logout", () => TypedResults.NoContent());

        // auth.md: "the signed-in user's memberships come from GET /api/v1/me". Memberships arrive
        // with tenant membership in Phase 2; for now this returns the Identity user only, and exists
        // as the first endpoint that actually requires a caller to be authenticated.
        app.MapGet("/api/v1/me", MeAsync).RequireAuthorization();
    }

    private static async Task<IResult> RegisterAsync(
        RegisterViewModel viewModel,
        IValidator<RegisterViewModel> validator,
        IAuthService authService,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(viewModel, ct);
        if (!validation.IsValid)
        {
            return TypedResults.ValidationProblem(validation.ToDictionary());
        }

        var result = await authService.RegisterAsync(viewModel, ct);
        return result.Succeeded
            ? TypedResults.Ok(result.Value)
            : TypedResults.ValidationProblem(result.Errors);
    }

    private static async Task<IResult> ChangePasswordAsync(
        ChangePasswordViewModel viewModel,
        IValidator<ChangePasswordViewModel> validator,
        IAuthService authService,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(viewModel, ct);
        if (!validation.IsValid)
        {
            return TypedResults.ValidationProblem(validation.ToDictionary());
        }

        var result = await authService.ChangePasswordAsync(viewModel, ct);
        return result.Succeeded
            ? TypedResults.NoContent()
            : TypedResults.ValidationProblem(result.Errors);
    }

    private static async Task<IResult> MeAsync(ClaimsPrincipal caller, UserManager<ApplicationUser> userManager)
    {
        var userId = userManager.GetUserId(caller);
        if (userId is null)
        {
            return TypedResults.Unauthorized();
        }

        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(new UserServiceModel
        {
            Id = user.Id,
            Email = user.Email!,
            DisplayName = user.DisplayName,
            CreatedAt = user.CreatedAt,
        });
    }
}
