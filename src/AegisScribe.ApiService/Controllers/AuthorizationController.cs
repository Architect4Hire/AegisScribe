using System.Net;
using System.Security.Claims;
using AegisScribe.Domain.Facade;
using AegisScribe.Domain.Managers.Models.ViewModels;
using Asp.Versioning;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace AegisScribe.ApiService.Controllers;

// OpenIddict's own protocol surface — connect/authorize and connect/token — named and shaped after
// OpenIddict's own samples (e.g. Velusia.Server.Controllers.AuthorizationController). The protocol
// shape stays here: the claims identity, SignIn/Forbid, the sign-in form, the OAuth wire format. Every
// question about a USER — does this password match, can they still sign in, what roles do they hold —
// goes through IAuthFacade like any other data access (backend.md); UserManager/SignInManager sit
// behind the user repository in AegisScribe.Domain. IOpenIddictApplicationManager stays: it is
// OpenIddict's own client registry, protocol plumbing rather than app data. No {version:apiVersion}
// segment on either route (set via SetAuthorizationEndpointUris/SetTokenEndpointUris in Program.cs) —
// these are OpenIddict's own paths, not this API's versioned business surface.
[ApiController]
[ApiVersionNeutral]
public class AuthorizationController(
    IOpenIddictApplicationManager applicationManager,
    IAuthFacade authFacade) : ControllerBase
{
    // The interactive half of the OAuth code+PKCE flow (auth.md: "the connect/authorize handler
    // resolves the current Identity user and issues the OpenIddict principal"). Every registered
    // client is first-party with ConsentType.Implicit, so the whole exchange fits in one request
    // round trip: GET renders a bare sign-in form carrying the original OAuth parameters as hidden
    // fields; POST re-submits them alongside a credential, checks it, and immediately signs the
    // OpenIddict principal in. Nothing is stored between the GET and the POST — no cookie, no
    // session, no antiforgery token anywhere in this project (auth.md's absolute restriction on the
    // API). A forged cross-site POST can't complete this anyway, since it can't supply the victim's
    // own credential.
    [HttpGet("~/connect/authorize")]
    [HttpPost("~/connect/authorize")]
    public async Task<IActionResult> Authorize(CancellationToken ct)
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenIddict server request could not be retrieved.");

        string? error = null;

        if (HttpMethods.IsPost(Request.Method))
        {
            var attempt = await authFacade.ValidateCredentialsAsync(new SignInViewModel
            {
                Email = Request.Form["identifier"].ToString(),
                Password = Request.Form["credential"].ToString(),
            }, ct);

            if (attempt.Subject is { } subject)
            {
                var identity = new ClaimsIdentity(
                    authenticationType: TokenValidationParameters.DefaultAuthenticationType,
                    nameType: Claims.Name,
                    roleType: Claims.Role);

                // sub and role only, nothing tenant-shaped — auth.md.
                identity.SetClaim(Claims.Subject, subject.UserId);
                identity.SetClaims(Claims.Role, [.. subject.Roles]);
                identity.SetScopes(request.GetScopes());
                identity.SetResources("aegisscribe-api");
                identity.SetDestinations(_ => [Destinations.AccessToken]);

                // A caller may request a shorter-than-configured lifetime (clamped, never longer)
                // — the same test-only knob the client_credentials exchange below exposes, needed
                // here to exercise the gateway's single-flight refresh without waiting out the
                // real default (1B.6). This override rides along as a private claim on the
                // authorization code's stored principal; Exchange() below deliberately carries it
                // forward only for the code redemption that follows this sign-in, not for any
                // later refresh.
                if (Request.Form["token_lifetime_seconds"].ToString() is { Length: > 0 } raw
                    && int.TryParse(raw, out var seconds) && seconds is > 0 and <= 900)
                {
                    identity.SetAccessTokenLifetime(TimeSpan.FromSeconds(seconds));
                }

                return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            }

            error = attempt.IsLockedOut
                ? "Too many failed attempts. This account is temporarily locked — try again in a few minutes."
                : "Incorrect email or password.";
        }

        return Content(RenderLoginForm(request, error), "text/html");
    }

    [HttpPost("~/connect/token")]
    public async Task<IActionResult> Exchange(CancellationToken ct)
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenIddict server request could not be retrieved.");

        // The interactive human flow: the code/refresh token already carries the principal the
        // Authorize() action issued. AuthenticateAsync both validates the code/refresh token itself
        // (expiry, single-use consumption, rotation — OpenIddict's own validation handler) and
        // returns that stored principal.
        if (request.IsAuthorizationCodeGrantType() || request.IsRefreshTokenGrantType())
        {
            var result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            var subject = await authFacade.GetSignInSubjectAsync(result.Principal!.GetClaim(Claims.Subject)!, ct);

            if (subject is null)
            {
                return Forbid(
                    authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                    }));
            }

            // A clean identity, not the stored principal's claims copied wholesale: OpenIddict's
            // SetAccessTokenLifetime (used by the token_lifetime_seconds test override above) is
            // itself stored as a private claim, so copying every claim forward would re-apply that
            // override — and whatever creation-timestamp claim rode along with it — to every future
            // refresh forever, producing an access token that's already expired the moment it's
            // minted. Re-populating from the current database state also means a revoked
            // PlatformAdmin takes effect on the next refresh, not just the next full login
            // (auth.md's "resolved per request" philosophy).
            var refreshedIdentity = new ClaimsIdentity(
                authenticationType: TokenValidationParameters.DefaultAuthenticationType,
                nameType: Claims.Name,
                roleType: Claims.Role);
            refreshedIdentity.SetClaim(Claims.Subject, subject.UserId);
            refreshedIdentity.SetClaims(Claims.Role, [.. subject.Roles]);
            refreshedIdentity.SetDestinations(_ => [Destinations.AccessToken]);

            // The authorization_code exchange is the one token issuance that follows directly from
            // Authorize()'s own SignIn — carry its test-only lifetime override forward here, since
            // it wouldn't otherwise survive onto a freshly built identity. Deliberately NOT done
            // for a refresh_token grant: this same branch handles every future refresh on the same
            // token chain, and re-applying a short-lived override there would mint an access token
            // that's already expired by the time it arrives.
            if (request.IsAuthorizationCodeGrantType() && result.Principal!.GetAccessTokenLifetime() is { } lifetime)
            {
                refreshedIdentity.SetAccessTokenLifetime(lifetime);
            }

            return SignIn(new ClaimsPrincipal(refreshedIdentity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        // client_credentials only: aegisscribe-ops (service-to-service) is the only client using
        // this grant. The interactive flow above serves the bff/mobile clients.
        if (!request.IsClientCredentialsGrantType())
        {
            return Forbid(
                authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                properties: new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.UnsupportedGrantType,
                }));
        }

        var application = await applicationManager.FindByClientIdAsync(request.ClientId!)
            ?? throw new InvalidOperationException("The calling client could not be found.");

        // Tokens carry sub and (for a human sign-in) PlatformAdmin only, nothing tenant-shaped —
        // auth.md. A machine client has no PlatformAdmin concept, so only sub/name go on here.
        var identity = new ClaimsIdentity(
            authenticationType: TokenValidationParameters.DefaultAuthenticationType,
            nameType: Claims.Name,
            roleType: Claims.Role);

        identity.SetClaim(Claims.Subject, await applicationManager.GetClientIdAsync(application));
        identity.SetClaim(Claims.Name, await applicationManager.GetDisplayNameAsync(application));

        // RFC 8707 resource indicator: defaults to this API's own audience, but a caller may name a
        // different one — how the wrong-audience validation scenario gets produced.
        var resources = request.GetResources();
        identity.SetResources(resources.Length > 0 ? resources : ["aegisscribe-api"]);

        // A caller may request a shorter-than-configured lifetime (clamped, never longer) — this is
        // what makes testing token expiry practical without waiting out the real default.
        if (request.GetParameter("token_lifetime_seconds")?.ToString() is { } lifetimeRaw
            && int.TryParse(lifetimeRaw, out var lifetimeSeconds) && lifetimeSeconds is > 0 and <= 900)
        {
            identity.SetAccessTokenLifetime(TimeSpan.FromSeconds(lifetimeSeconds));
        }

        identity.SetDestinations(_ => [Destinations.AccessToken]);

        return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    // Every present OAuth parameter round-trips as a hidden field so the POST reconstructs the
    // identical authorization request (OpenIddict parses a POST from the form body only, not the
    // query string — OpenIddictServerAspNetCoreHandlers.ExtractGetOrPostRequest).
    private static string RenderLoginForm(OpenIddictRequest request, string? error)
    {
        var hidden = new Dictionary<string, string?>
        {
            [Parameters.ClientId] = request.ClientId,
            [Parameters.RedirectUri] = request.RedirectUri,
            [Parameters.ResponseType] = request.ResponseType,
            [Parameters.Scope] = request.Scope,
            [Parameters.State] = request.State,
            [Parameters.CodeChallenge] = request.CodeChallenge,
            [Parameters.CodeChallengeMethod] = request.CodeChallengeMethod,
            [Parameters.Nonce] = request.Nonce,
        };

        var hiddenInputs = string.Concat(hidden
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .Select(kv => $"""<input type="hidden" name="{Encode(kv.Key)}" value="{Encode(kv.Value!)}" />"""));

        var errorHtml = error is null ? "" : $"""<p class="form-error">{Encode(error)}</p>""";

        // Plain server-rendered HTML/CSS, no client-side framework or build step — this page sits in
        // the middle of an OAuth redirect chain and has to work with JavaScript off (1B.4d). The two
        // stylesheets are a transcription of design/aegisscribe-armory.html's tokens and primitives,
        // living under this project's own wwwroot since it can't reach into src/web's SCSS pipeline.
        return $"""
            <!doctype html>
            <html lang="en">
            <head>
                <meta charset="utf-8" />
                <meta name="viewport" content="width=device-width, initial-scale=1" />
                <title>Sign in — AegisScribe</title>
                <link rel="preconnect" href="https://fonts.googleapis.com" />
                <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin />
                <link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Noto+Sans:wght@400;500;600&family=Barlow+Semi+Condensed:wght@400;500;600;700&family=Cinzel:wght@600&display=swap" />
                <link rel="stylesheet" href="/css/tokens.css" />
                <link rel="stylesheet" href="/css/sign-in.css" />
            </head>
            <body>
                <main class="sign-in-card">
                    <img class="wordmark" src="/fulllogo.png" alt="AegisScribe" width="1254" height="1254" />
                    {errorHtml}
                    <form method="post" action="/connect/authorize">
                        {hiddenInputs}
                        <label class="label" for="identifier">Email</label>
                        <div class="field"><input id="identifier" type="email" name="identifier" required autocomplete="username" /></div>
                        <label class="label" for="credential">Password</label>
                        <div class="field"><input id="credential" type="password" name="credential" required autocomplete="current-password" /></div>
                        <button class="btn btn-primary" type="submit">Sign in</button>
                    </form>
                </main>
            </body>
            </html>
            """;
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
