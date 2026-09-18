using System.Globalization;
using AegisScribe.Domain.Business;
using AegisScribe.Domain.Managers.Models.Domain;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace AegisScribe.ApiService.Infrastructure;

// Turns the exceptions the domain layers throw into HTTP — neither Facades nor Business know about
// status codes. Anything not listed falls through to the default handler as a 500.
//
// Every `type` URI below is the contract and may never be repurposed; `title` and `detail` are for
// humans and can be reworded freely (api-contract.md). The 409s all share a shape: the request is
// well-formed and the row exists, and what refuses it is the state of something else.
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
                // A collision with an existing row rather than a malformed field, so 409 rather than a
                // validation 400 — the rank form shows it against the name input.
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
                        // second round-trip. Null when they have set none, and null when the unique
                        // index rather than the pre-check caught the race. No user id and no email —
                        // the name is all any member can already see through the claim GET.
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
                        // CLAIMED are different facts, and a client branches on which — one sends you
                        // to the roster, the other to whoever holds the claim.
                        Type = "https://api.aegisscribe.com/problems/character-already-on-roster",
                        Title = "That character is already on the roster",
                        Status = StatusCodes.Status409Conflict,
                        Detail = "This community's roster already includes that character.",
                    },
                });

            case RosterEntryHasAltsException hasAlts:
                // Removing the entry would detach somebody's other characters as a side effect, which
                // the officer doing it cannot see.
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
                httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                return true;

            case ClaimNotYoursException:
                // Bare 403, no body: the caller is a member in good standing, the row just isn't
                // theirs, and there is nothing for a client to branch on beyond the status. An officer
                // wanting this outcome uses their own route.
                httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                return true;

            case RankInUseException rankInUse:
                httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
                return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = httpContext,
                    Exception = exception,
                    ProblemDetails = new ProblemDetails
                    {
                        // Distinct from tenant-rank-name-taken: both are 409s on the rank surface and a
                        // client branches on the difference — one sends you back to the name field, the
                        // other to the roster.
                        Type = "https://api.aegisscribe.com/problems/tenant-rank-in-use",
                        Title = "That rank is still in use",
                        Status = StatusCodes.Status409Conflict,
                        // The holder count is in `detail` rather than only in prose: an officer told to
                        // "reassign these first" needs to know how many there are.
                        Detail = $"{rankInUse.HolderCount} roster entries still hold this rank. " +
                            "Move them to another rank before deleting it.",
                    },
                });

            case SyncBudgetExhaustedException budgetExhausted:
                // 429 with a retry hint, never a silent queue (external.md). Retry-After is the part
                // that matters: without it a client backs off for an unknown length of time, producing
                // exactly the retry storm the limit exists to prevent.
                httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                httpContext.Response.Headers.RetryAfter =
                    ((int)Math.Ceiling(budgetExhausted.RetryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

                return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = httpContext,
                    Exception = exception,
                    ProblemDetails = new ProblemDetails
                    {
                        // A client branches on this to tell "your community is out of budget" from the
                        // API's own per-IP throttling, which is also a 429 and means something else.
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
                // A community must always have an Owner (auth.md). A branded type rather than a bare
                // 403, because the member-management screen has something specific to say here —
                // "promote somebody else first" — and it is the one refusal on this surface that is
                // about the community's shape rather than about the caller's rank.
                httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = httpContext,
                    Exception = exception,
                    ProblemDetails = new ProblemDetails
                    {
                        Type = "https://api.aegisscribe.com/problems/last-owner",
                        Title = "A community must always have an owner",
                        Status = StatusCodes.Status403Forbidden,
                        Detail = "Promote another member to Owner before demoting or removing this one.",
                    },
                });

            case MembershipActionNotPermittedException notPermitted:
                // Distinct from last-owner: this one IS about the caller's rank relative to the
                // target's, and a client branches on the difference — one says "ask an owner", the
                // other says "promote somebody first".
                httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = httpContext,
                    Exception = exception,
                    ProblemDetails = new ProblemDetails
                    {
                        Type = "https://api.aegisscribe.com/problems/membership-action-not-permitted",
                        Title = "You may not do that to this member",
                        Status = StatusCodes.Status403Forbidden,
                        Detail = notPermitted.Message,
                    },
                });

            case InvitationNotFoundException:
                // 404 and an empty body. A token that never existed stays indistinguishable from
                // nothing at all — the three refusals below are distinguishable on purpose, this one
                // deliberately is not.
                httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
                return true;

            case InvitationUnusableException unusable:
                // 410 Gone, and a DISTINCT type per reason: 8.3b requires expired, consumed and
                // revoked to be told apart, because what the holder should do next differs — ask for a
                // fresh link, nothing, or talk to an officer.
                //
                // Not one of these carries the community's name. A dead token buys its holder nothing,
                // including the knowledge of what it was for.
                httpContext.Response.StatusCode = StatusCodes.Status410Gone;
                return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = httpContext,
                    Exception = exception,
                    ProblemDetails = new ProblemDetails
                    {
                        Type = unusable.Reason switch
                        {
                            InvitationRefusal.Expired => "https://api.aegisscribe.com/problems/invitation-expired",
                            InvitationRefusal.Revoked => "https://api.aegisscribe.com/problems/invitation-revoked",
                            _ => "https://api.aegisscribe.com/problems/invitation-consumed",
                        },
                        Title = "That invitation cannot be used",
                        Status = StatusCodes.Status410Gone,
                        Detail = unusable.Message,
                    },
                });

            case AlreadyAMemberException:
                httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
                return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = httpContext,
                    Exception = exception,
                    ProblemDetails = new ProblemDetails
                    {
                        Type = "https://api.aegisscribe.com/problems/already-a-member",
                        Title = "Already a member",
                        Status = StatusCodes.Status409Conflict,
                        Detail = exception.Message,
                    },
                });

            case JoinRequestAlreadyPendingException:
                httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
                return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = httpContext,
                    Exception = exception,
                    ProblemDetails = new ProblemDetails
                    {
                        Type = "https://api.aegisscribe.com/problems/join-request-already-pending",
                        Title = "You have already asked to join",
                        Status = StatusCodes.Status409Conflict,
                        Detail = exception.Message,
                    },
                });

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
