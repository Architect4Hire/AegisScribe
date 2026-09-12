using AegisScribe.Domain.Managers.Models.Domain;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace AegisScribe.ApiService.Infrastructure;

// Turns the exceptions the domain layers throw into HTTP. Facades throw ValidationException, Business
// throws domain exceptions; neither knows about status codes. The 400s keep the ValidationProblemDetails
// shape the controllers used to build by hand (per-field/per-code errors), so a client's error handling
// is unchanged. Anything not listed falls through to the default handler as a 500.
public sealed class DomainExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        switch (exception)
        {
            case ValidationException validation:
                return await WriteValidationProblemAsync(httpContext, exception, validation.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()));

            case DomainValidationException domainValidation:
                return await WriteValidationProblemAsync(httpContext, exception, domainValidation.Errors);

            case AuthenticationRequiredException:
                // Bare, bodiless 401 — identical to what [Authorize] returns, so "no token" and "a
                // token for a user who no longer exists" look the same.
                httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return true;

            case TenantStampMismatchException:
                // Never triggered by a well-formed request — TenantId isn't client-supplied. Still a
                // bare 403, not a 500, so it doesn't read as an unhandled server fault in monitoring.
                httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                return true;

            case LastOwnerException:
                // Unused placeholder until the demote/remove membership work lands (auth.md) — mapped
                // now so that work only needs to add the throw, not a new case here.
                httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                return true;

            default:
                return false;
        }
    }

    private ValueTask<bool> WriteValidationProblemAsync(
        HttpContext httpContext, Exception exception, IDictionary<string, string[]> errors)
    {
        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        return problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ValidationProblemDetails(errors) { Status = StatusCodes.Status400BadRequest },
        });
    }
}
