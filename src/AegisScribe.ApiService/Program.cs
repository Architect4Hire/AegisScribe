using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;
using AegisScribe.ApiService.Auth;
using AegisScribe.ApiService.Infrastructure;
using AegisScribe.ApiService.Infrastructure.Idempotency;
using AegisScribe.ApiService.Tenancy;
using AegisScribe.Domain;
using AegisScribe.Domain.Context;
using AegisScribe.Domain.Data;
using AegisScribe.Domain.Integration;
using AegisScribe.Domain.Integration.Blizzard;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.Identity;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Backs IIdempotencyStore (Idempotency-Key replay for POSTs that create something — api-contract.md).
// Same integration Gateway already uses for its session store; api.csproj/AppHost now reference it too.
builder.AddRedisDistributedCache("cache");

// ITenantContext is a scoped, per-request dependency now that OnModelCreating reads it for the
// tenant-scoped query filter (2.4) — AddSqlServerDbContext always pools in this Aspire version (its
// settings type has no pooling toggle, confirmed against the installed package), and pooling would
// freeze the FIRST request's instance into the pooled context and silently reuse it for every later
// request. That is tenancy.md's worst failure mode, via a completely different mechanism than a
// missing filter. Plain AddDbContext + EnrichSqlServerDbContext is Aspire's documented unpooled
// alternative — same retries/health checks/telemetry, without AddDbContextPool underneath.
builder.Services.AddDbContext<AegisScribeDbContext>((sp, options) =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("aegisscribedb"));
    options.UseOpenIddict();
    options.AddInterceptors(sp.GetRequiredService<TenantStampingInterceptor>());
});
builder.EnrichSqlServerDbContext<AegisScribeDbContext>();

// Add services to the container.

// Controller → Facade → Business → DataLayer is the whole HTTP surface (backend.md, the
// add-endpoint skill) — no minimal-API route mapping for anything beyond framework-provided
// endpoints like health checks.
// api-contract.md: "Enums cross the wire as strings, never as integers." TenantRole
// (TenantMembershipServiceModel.Role) is the first enum any response has carried — global so every
// future one gets this for free. Two registrations, because MVC's wire serialization
// (Mvc.JsonOptions, via AddJsonOptions) and the native OpenAPI document's schema generation
// (Http.Json.JsonOptions, via ConfigureHttpJsonOptions) read from two separate JsonSerializerOptions
// instances — setting only one leaves the actual response and its documented schema disagreeing.
builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

// URL segment only (api-contract.md forbids header/query-string versioning) — AddApiVersioning()'s
// own default reader reads BOTH query string and URL segment unless overridden here. AddMvc() wires
// versioning into controller action selection; AddOpenApi() must come after
// AddApiVersioning()/AddApiExplorer() in this chain to pick up Asp.Versioning's version-aware
// variant instead of the plain one.
builder.Services.AddApiVersioning(options =>
    {
        options.ApiVersionReader = new UrlSegmentApiVersionReader();
        options.ReportApiVersions = true; // emits api-supported-versions / api-deprecated-versions

        // Deprecation/Sunset support for later: Asp.Versioning has this built in as a policy builder —
        // options.Policies.Deprecate(1.0).Effective(...).Link(...).Title(...).Type(...) and
        // .Sunset(1.0).Effective(...) — emitted as real RFC 8594 headers once a version actually needs
        // retiring. Nothing to configure now; v1 is the only version and isn't deprecated.
    })
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'VVV"; // formats 1.0 as "v1", matching the existing URLs
        options.SubstituteApiVersionInUrl = true; // required specifically for URL-segment versioning
    })
    .AddMvc()
    .AddOpenApi();

builder.Services.AddDataProtection();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;

        // The API's principals come from OpenIddict tokens, which identify the user by "sub" — not
        // by Identity's default NameIdentifier claim, which those tokens never carry. Without this,
        // UserManager.GetUserId and ICurrentUser.UserId both come back null for every signed-in
        // caller: /me 401s and tenant resolution 404s every member.
        options.ClaimsIdentity.UserIdClaimType = OpenIddictConstants.Claims.Subject;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AegisScribeDbContext>()
    .AddDefaultTokenProviders()
    // AddIdentityCore (unlike AddIdentity) does not register SignInManager on its own — the user
    // repository (AegisScribe.Domain) needs CheckPasswordSignInAsync for the interactive sign-in leg.
    .AddSignInManager();

// OpenIddict is both the token issuer (server) and, as of this phase, the API's own validator —
// AddValidation(o => o.UseLocalServer()) shares keys directly since issuer and resource server are
// the same process here, rather than fetching a JWKS. Enabling the authorization/token endpoint
// passthroughs makes those routes reachable via Controllers/AuthorizationController.cs, which
// handles the interactive sign-in flow (1B.6) alongside the client_credentials grant aegisscribe-ops
// uses.
builder.Services.AddOpenIddict()
    .AddCore(options => options.UseEntityFrameworkCore().UseDbContext<AegisScribeDbContext>())
    .AddServer(options =>
    {
        options.SetAuthorizationEndpointUris("connect/authorize")
            .SetTokenEndpointUris("connect/token")
            .SetRevocationEndpointUris("connect/revoke")
            .SetEndSessionEndpointUris("connect/logout");

        // A static issuer, not the default per-request-computed one — mandatory once
        // UseForwardedHeaders() is in the mix (1B.7): the interactive flow arrives proxied through
        // the gateway (X-Forwarded-Proto rewrites the scheme to https), but the gateway's own
        // backchannel calls to connect/token (refresh, connect/revoke) go straight to the API over
        // its internal http address and never pass through YARP at all — so the computed issuer
        // would differ by which path minted the token, and OpenIddict's own validation correctly
        // rejects a token whose issuer doesn't match. Confirmed against OpenIddict's Zirku sample,
        // which carries the identical warning for its mTLS aliasing scenario. This is a stable
        // placeholder, not a real reachable address — UseLocalServer() validates by shared keys in
        // this same process, never by dereferencing the issuer over the network — and becomes the
        // real https://api.aegisscribe.com once an actual deployment exists.
        options.SetIssuer(new Uri("https://api.aegisscribe.internal/"));

        options.AllowAuthorizationCodeFlow().RequireProofKeyForCodeExchange()
            .AllowRefreshTokenFlow()
            // Needed for aegisscribe-ops (client credentials) — one of the three clients this
            // phase registers; the two interactive flows above serve the bff/mobile clients.
            .AllowClientCredentialsFlow();

        // OpenIddict's built-in resource allow-list (RegisterResources) exists for a fleet of
        // distinct downstream resource servers; there is exactly one here ("aegisscribe-api"), and
        // it enforces its own audience in AddValidation below. Without this, a caller naming any
        // other resource is rejected by the protocol layer before minting — which would make the
        // wrong-audience scenario (a token that mints fine but the API rejects) unreachable.
        options.DisableResourceValidation();

        // A plain config knob (not test-only): a caller may request a shorter lifetime per-token
        // (see TokenEndpoints.cs), clamped to never exceed this configured default.
        options.SetAccessTokenLifetime(
            TimeSpan.FromSeconds(builder.Configuration.GetValue("Oidc:AccessTokenLifetimeSeconds", 900)));

        if (builder.Environment.IsDevelopment())
        {
            options.AddDevelopmentEncryptionCertificate()
                .AddDevelopmentSigningCertificate();
        }
        else
        {
            options.AddEncryptionCertificate(LoadOidcCertificate(builder.Configuration["Oidc:EncryptionCertificate"]))
                .AddSigningCertificate(LoadOidcCertificate(builder.Configuration["Oidc:SigningCertificate"]));
        }

        options.UseAspNetCore()
            .EnableAuthorizationEndpointPassthrough()
            .EnableTokenEndpointPassthrough();
    })
    .AddValidation(options =>
    {
        options.AddAudiences("aegisscribe-api");
        options.UseLocalServer();
        options.UseAspNetCore();
    });

// OpenIddict's access tokens are encrypted JWTs (JWE) — AddJwtBearer cannot read them at all (a
// silent 401 with nothing logged), so the default scheme is OpenIddict's own validation handler,
// registered above by AddValidation(). No cookie auth, no login form, no session, no antiforgery —
// this is a pure token resource server (auth.md).
builder.Services.AddAuthentication(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AuthPolicies.PlatformAdmin, policy => policy.RequireRole(AuthPolicies.PlatformAdmin))
    .AddPolicy(AuthPolicies.TenantMember, policy => policy.AddRequirements(new TenantRoleRequirement(TenantRole.Member)))
    .AddPolicy(AuthPolicies.TenantOfficer, policy => policy.AddRequirements(new TenantRoleRequirement(TenantRole.Officer)))
    .AddPolicy(AuthPolicies.TenantOwner, policy => policy.AddRequirements(new TenantRoleRequirement(TenantRole.Owner)));

// Every layer below the controllers — facades, business, data layers, repositories, validators, and
// the ambient TenantContext — lives in AegisScribe.Domain and registers itself (backend.md -> "Two
// projects, one direction"). The host supplies what is host-shaped: the DbContext and Identity above,
// and the caller, read from the validated token.
builder.Services.AddAegisScribeDomain();

// Registered on its own rather than from AddAegisScribeDomain: the Blizzard integration brings its own
// typed client and token lifecycle, and the sync worker will want it without the request stack (6.5).
// Credentials arrive as Blizzard__ClientId / Blizzard__ClientSecret from the AppHost, and their absence
// is a supported state — the gateway degrades and this host still starts (external.md).
builder.Services.AddBlizzardIntegration();

// The per-tenant fairness budget (6.6). Separate from the Blizzard section because it limits a
// different thing: that one is the contractual cap on the whole process, this one is one community's
// share of tenant-triggered work.
builder.Services.AddOptions<TenantSyncBudgetOptions>()
    .BindConfiguration(TenantSyncBudgetOptions.SectionName)
    .Validate(
        options => options.IsValid,
        "TenantSyncBudget:CallsPerWindow and Window must both be positive — a non-positive budget " +
        "refuses every tenant-triggered sync rather than removing the limit.")
    .ValidateOnStart();

// Tagged external so it reports at /health/external and stays out of /health: no Blizzard credentials is
// normal locally, and it must not make the API look unready.
builder.Services.AddHealthChecks()
    .AddCheck<BlizzardGatewayHealthCheck>("blizzard", tags: [HealthCheckTags.External]);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddScoped<IAuthorizationHandler, TenantRoleAuthorizationHandler>();
builder.Services.AddScoped<IIdempotencyStore, RedisIdempotencyStore>();

// Facades throw ValidationException and Business throws domain exceptions; this is where they
// become problem+json (or a bare 401). Controllers never catch them.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();

// Partitioned by who's calling, in order: authenticated sub, then OAuth client_id (a fleet-wide
// budget — many devices share one client_id), then IP only for genuinely anonymous requests. Never
// falls back to IP once either claim is present — a carrier NAT puts a whole city behind one address
// (backend.md -> "The API's public edge"). No request carries a real sub/client_id until 1B.5 wires
// token validation, so today everything lands in the IP bucket; the shape is correct ahead of that.
//
// Fixed window, not sliding: System.Threading.RateLimiting's SlidingWindowRateLimiter doesn't
// reliably populate the RetryAfter lease metadata when QueueLimit is 0 (dotnet/runtime#131175),
// and a 429 without Retry-After fails the one thing this is required to do.
// Read once at startup rather than per request: the partitioners below run on every request, and an
// IOptions resolution inside one would be overhead for a value that cannot change without a restart.
// Absent configuration these are exactly the literals this block used to carry — see RateLimitOptions.
var rateLimits = builder.Configuration.GetSection(RateLimitOptions.SectionName).Get<RateLimitOptions>()
    ?? new RateLimitOptions();

if (!rateLimits.IsValid)
{
    // Fails startup rather than serving traffic with a broken edge. A non-positive permit limit does
    // not relax the limiter, it rejects everything — and a zero window divides by nothing.
    throw new InvalidOperationException(
        "RateLimits configuration is invalid: every permit limit and the window must be positive.");
}

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = (context, _) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
        }

        return ValueTask.CompletedTask;
    };

    // Stacks on top of the global limiter above. The slug-check endpoint is authenticated, so `sub` is
    // normally present; the IP fallback is there so a misconfiguration degrades to a bucket rather
    // than to no limit at all.
    options.AddPolicy(RateLimiterPolicies.SlugCheck, httpContext =>
    {
        var partition = httpContext.User.FindFirst("sub")?.Value
            ?? $"ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";

        return RateLimitPartition.GetFixedWindowLimiter($"slug-check:{partition}", _ =>
            new FixedWindowRateLimiterOptions
            {
                PermitLimit = rateLimits.SlugCheckPermitLimit,
                Window = rateLimits.Window,
                QueueLimit = 0,
            });
    });

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        var sub = httpContext.User.FindFirst("sub")?.Value;
        if (!string.IsNullOrEmpty(sub))
        {
            return RateLimitPartition.GetFixedWindowLimiter($"sub:{sub}", _ =>
                new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rateLimits.AuthenticatedPermitLimit,
                    Window = rateLimits.Window,
                    QueueLimit = 0,
                });
        }

        var clientId = httpContext.User.FindFirst("client_id")?.Value;
        if (!string.IsNullOrEmpty(clientId))
        {
            return RateLimitPartition.GetFixedWindowLimiter($"client:{clientId}", _ =>
                new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rateLimits.ClientPermitLimit,
                    Window = rateLimits.Window,
                    QueueLimit = 0,
                });
        }

        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter($"ip:{ip}", _ =>
            new FixedWindowRateLimiterOptions
            {
                PermitLimit = rateLimits.AnonymousPermitLimit,
                Window = rateLimits.Window,
                QueueLimit = 0,
            });
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await RoleSeeder.SeedPlatformAdminRoleAsync(scope.ServiceProvider);
}

// Configure the HTTP request pipeline.
// Non-production, not just Development — no third-party consumers exist to serve a public schema
// in any deployed environment, and this is what makes it 404 in Production (backend.md).
if (!app.Environment.IsProduction())
{
    app.MapOpenApi().WithDocumentPerVersion();
}

// Must run before UseHttpsRedirection (and anything else depending on scheme/host) so it can
// restore the original public scheme from the gateway's X-Forwarded-Proto before those middlewares
// see the request (host-and-deploy/proxy-load-balancer's own ordering guidance) — otherwise the
// internal gateway->API hop, which is plain HTTP, looks like an insecure request and gets redirected.
// KnownNetworks/KnownProxies are left at their framework defaults (loopback only, verified against
// the current docs): gateway and API run on the same machine in this local-first setup (aspire.md),
// so "trust only the gateway's network" and "trust loopback" are the same statement today. A real
// multi-host deployment would need this widened to the gateway's actual address/subnet — a
// follow-up, not solved here, since no such deployment exists yet.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
});

// Early, so it wraps everything that can throw a domain exception — the tenant-resolution middleware
// as well as the controllers. DomainExceptionHandler decides what it handles; the rest are 500s.
app.UseExceptionHandler();

app.UseHttpsRedirection();

// Serves wwwroot/css/{tokens,sign-in}.css for the connect/authorize sign-in page (1B.4d) — the one
// screen in the app that's server-rendered HTML instead of the Angular bundle, so it needs its own
// static assets. Anonymous and unauthenticated by nature; placed before UseAuthentication so a request
// for these two files never runs through the pipeline built for API routes.
app.UseStaticFiles();

app.UseAuthentication();
// After authentication so HttpContext.User carries whatever claims a token has — even one that
// will later fail authorization — for the rate limiter's sub/client_id partitioning above.
app.UseRateLimiter();
// After authentication (needs HttpContext.User to check membership) and before authorization (a
// tenant-aware policy, added later, will read the ITenantContext this populates) — tenancy.md.
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseAuthorization();

app.MapControllers();

app.MapDefaultEndpoints();

app.Run();

// No real non-Development environment exists for this project yet, so this path is unexercised —
// it exists so a certificate is selected BY ENVIRONMENT rather than the dev helper being called
// unconditionally. Expects a base64-encoded PFX; production loading (Key Vault, mounted secret,
// etc.) is a deployment-specific decision this app doesn't make yet.
static X509Certificate2 LoadOidcCertificate(string? base64Pfx)
{
    if (string.IsNullOrEmpty(base64Pfx))
    {
        throw new InvalidOperationException(
            "No OpenIddict certificate configured for a non-Development environment.");
    }

    return X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(base64Pfx), password: null);
}
