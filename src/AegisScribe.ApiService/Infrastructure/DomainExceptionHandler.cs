using System.Globalization;
using AegisScribe.Domain.Business;
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

            case SlugTakenException:
                // The one error here that carries a body, because it is the one a client must branch
                // on: the create form retries with a different slug. The `type` URI is the contract
                // and may never be repurposed (api-contract.md); `title` is for humans.
                httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
                return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = httpContext,
                    Exception = exception,
                    ProblemDetails = new ProblemDetails
                    {
                        Type = "https://api.aegisscribe.com/problems/tenant-slug-taken",
                        Title = "That community address was just taken",
                        Status = StatusCodes.Status409Conflict,
                        Detail = "Another community claimed this slug while the request was in flight. Choose a different one.",
                    },
                });

            case RankNameTakenException rankNameTaken:
                // 409 rather than a validation 400, because this is a collision with a row that
                // already exists rather than a malformed field — the rank form shows it against the
                // name input and offers a different one. The `type` URI is the contract and may never
                // be repurposed (api-contract.md).
                httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
                return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = httpContext,
                    Exception = exception,
                    ProblemDetails = new ProblemDetails
                    {
                        Type = "https://api.aegisscribe.com/problems/tenant-rank-name-taken",
                        Title = "That rank name is already in use",
                        Status = StatusCodes.Status409Conflict,
                        Detail = $"This community already has a rank named '{rankNameTaken.Name}'. Choose a different name.",
                    },
                });

            case CharacterAlreadyClaimedException alreadyClaimed:
                // 409: the request is well-formed and the character exists — what refuses it is
                // somebody else already holding the claim. Never a silent overwrite (7.2b).
                httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
                return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = httpContext,
                    Exception = exception,
                    ProblemDetails = new ProblemDetails
                    {
                        Type = "https://api.aegisscribe.com/problems/character-already-claimed",
                        Title = "That character is already claimed",
                        Status = StatusCodes.Status409Conflict,
                        Detail = "Another member of this community has claimed this character. " +
                            "An officer can free it.",
                        // The holder's display name, so the conflict dialog can name them without a
                        // second round-trip. An extension member is a contract promise for the life of
                        // v1 (api-contract.md); null when they have set no display name, and null when
                        // the unique index rather than the pre-check caught the race. No user id and
                        // no email — the name is all any member can already see through the claim GET.
                        Extensions = { ["claimedByDisplayName"] = alreadyClaimed.ClaimedByDisplayName },
                    },
                });

            case CharacterAlreadyOnRosterException:
                httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
                return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = httpContext,
                    Exception = exception,
                    ProblemDetails = new ProblemDetails
                    {
                        // Distinct from character-already-claimed: being ON the roster and being
                        // CLAIMED by someone are different facts, and a client branches on which — one
                        // sends you to the roster, the other to whoever holds the claim.
                        Type = "https://api.aegisscribe.com/problems/character-already-on-roster",
                        Title = "That character is already on the roster",
                        Status = StatusCodes.Status409Conflict,
                        Detail = "This community's roster already includes that character.",
                    },
                });

            case RosterEntryHasAltsException hasAlts:
                // 409: the request is well-formed and the entry exists — what refuses it is the alts
                // hanging off it. Removing it would detach somebody's other characters as a side
                // effect, which the officer doing it cannot see.
                httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
                return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = httpContext,
                    Exception = exception,
                    ProblemDetails = new ProblemDetails
                    {
                        Type = "https://api.aegisscribe.com/problems/roster-entry-has-alts",
                        Title = "That character has alts linked to it",
                        Status = StatusCodes.Status409Conflict,
                        // The count is absent when the foreign key caught a race rather than the
                        // pre-check catching a steady state — see RosterEntryHasAltsException.
                        Detail = hasAlts.AltCount is null
                            ? "Characters are linked to this one as alts. Detach them before removing it."
                            : $"{hasAlts.AltCount} characters are linked to this one as alts. " +
                                "Detach them before removing it.",
                    },
                });

            case AltDepthException altDepth:
                // 409: the request is well-formed and both characters exist — what refuses it is the
                // shape of the roster. The alt model is one level deep (7.3), and holding that line is
                // what makes cycles impossible rather than merely unlikely.
                httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
                return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = httpContext,
                    Exception = exception,
                    ProblemDetails = new ProblemDetails
                    {
                        // One `type` for both causes: a client branches on "this is a depth problem"
                        // and shows the remedy, which differs by cause and travels in `detail`.
                        Type = "https://api.aegisscribe.com/problems/alt-depth-exceeded",
                        Title = "Alts only nest one level deep",
                        Status = StatusCodes.Status409Conflict,
                        Detail = altDepth.Message,
                    },
                });

            case AltLinkNotPermittedException:
                // Bare 403, like ClaimNotYoursException below and for the same reason: the caller is a
                // member in good standing, the characters just aren't theirs.
                httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                return true;

            case ClaimNotYoursException:
                // Bare 403. The add-endpoint skill's canonical resource-authorization outcome — the
                // caller is a member in good standing, the row just isn't theirs. No body, because
                // there is nothing for a client to branch on beyond the status: an officer wanting
                // this outcome uses their own route.
                httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                return true;

            case RankInUseException rankInUse:
                // 409, because the request is well-formed and the rank exists — what refuses it is the
                // state of the roster. The holder count is in `detail` rather than only in prose: an
                // officer being told "reassign these first" needs to know how many there are.
                httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
                return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = httpContext,
                    Exception = exception,
                    ProblemDetails = new ProblemDetails
                    {
                        // Distinct from tenant-rank-name-taken: both are 409s on the rank surface and
                        // a client branches on the difference — one sends you back to the name field,
                        // the other to the roster. A `type` URI may never be repurposed
                        // (api-contract.md).
                        Type = "https://api.aegisscribe.com/problems/tenant-rank-in-use",
                        Title = "That rank is still in use",
                        Status = StatusCodes.Status409Conflict,
                        Detail = $"{rankInUse.HolderCount} roster entries still hold this rank. " +
                            "Move them to another rank before deleting it.",
                    },
                });

            case SyncBudgetExhaustedException budgetExhausted:
                // 429 with a retry hint, never a silent queue (external.md). Retry-After is the part
                // that matters: a 429 without one tells a client to back off without saying for how
                // long, which produces exactly the retry storm the limit exists to prevent — the same
                // reasoning the rate limiter's OnRejected already follows.
                httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                httpContext.Response.Headers.RetryAfter =
                    ((int)Math.Ceiling(budgetExhausted.RetryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

                return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = httpContext,
                    Exception = exception,
                    ProblemDetails = new ProblemDetails
                    {
                        // The `type` URI is the contract and may never be repurposed (api-contract.md).
                        // A client branches on this to tell "your community is out of budget" from the
                        // API's own per-IP throttling, which is also a 429 and means something else
                        // entirely.
                        Type = "https://api.aegisscribe.com/problems/tenant-sync-budget-exhausted",
                        Title = "This community has used its sync budget",
                        Status = StatusCodes.Status429TooManyRequests,
                        Detail = $"The budget of {budgetExhausted.Limit} Blizzard calls for the current " +
                            "window is spent. Background refresh is unaffected and continues.",
                    },
                });

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
