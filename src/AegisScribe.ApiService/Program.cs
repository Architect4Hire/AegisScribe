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

// Backs IIdempotencyStore — Idempotency-Key replay for POSTs that create something (api-contract.md).
builder.AddRedisDistributedCache("cache");

// Unpooled deliberately: OnModelCreating reads the scoped ITenantContext, and AddSqlServerDbContext
// always pools in this Aspire version — which would freeze the FIRST request's tenant into the pooled
// context and reuse it for every later one (tenancy.md's worst failure mode).
builder.Services.AddDbContext<AegisScribeDbContext>((sp, options) =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("aegisscribedb"));
    options.UseOpenIddict();
    options.AddInterceptors(sp.GetRequiredService<TenantStampingInterceptor>());
});
builder.EnrichSqlServerDbContext<AegisScribeDbContext>();

// Enums cross the wire as strings (api-contract.md). Two registrations, because MVC serialization and
// OpenAPI schema generation read separate JsonSerializerOptions — setting one leaves the response and
// its documented schema disagreeing.
builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

// URL segment only — the default reader also accepts a query string unless overridden
// (api-contract.md). AddOpenApi() must come last, to pick up the version-aware variant.
builder.Services.AddApiVersioning(options =>
    {
        options.ApiVersionReader = new UrlSegmentApiVersionReader();
        options.ReportApiVersions = true;
    })
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'VVV";
        options.SubstituteApiVersionInUrl = true; // required specifically for URL-segment versioning
    })
    .AddMvc()
    .AddOpenApi();

builder.Services.AddDataProtection();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;

        // OpenIddict tokens identify the user by "sub", not Identity's default NameIdentifier. Without
        // this, UserManager.GetUserId is null for every signed-in caller.
        options.ClaimsIdentity.UserIdClaimType = OpenIddictConstants.Claims.Subject;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AegisScribeDbContext>()
    .AddDefaultTokenProviders()
    // AddIdentityCore, unlike AddIdentity, does not register SignInManager; the user repository needs
    // CheckPasswordSignInAsync.
    .AddSignInManager();

// Both the token issuer and the API's own validator: UseLocalServer() shares keys in-process rather
// than fetching a JWKS, and the passthroughs hand connect/* to AuthorizationController.
builder.Services.AddOpenIddict()
    .AddCore(options => options.UseEntityFrameworkCore().UseDbContext<AegisScribeDbContext>())
    .AddServer(options =>
    {
        options.SetAuthorizationEndpointUris("connect/authorize")
            .SetTokenEndpointUris("connect/token")
            .SetRevocationEndpointUris("connect/revoke")
            .SetEndSessionEndpointUris("connect/logout");

        // Static, not the default per-request-computed issuer: the gateway's backchannel token calls
        // bypass YARP, so a computed issuer would differ by which path minted the token and OpenIddict
        // would reject its own. Never dereferenced — UseLocalServer() validates by shared keys.
        options.SetIssuer(new Uri("https://api.aegisscribe.internal/"));

        options.AllowAuthorizationCodeFlow().RequireProofKeyForCodeExchange()
            .AllowRefreshTokenFlow()
            // For aegisscribe-ops; the interactive flows above serve the bff and mobile clients.
            .AllowClientCredentialsFlow();

        // One resource server, enforcing its own audience in AddValidation below. The built-in
        // allow-list would reject a wrong-audience request before minting, making that scenario — a
        // token that mints fine but the API rejects — untestable.
        options.DisableResourceValidation();

        // A caller may request a shorter per-token lifetime, clamped to never exceed this default.
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

// OpenIddict's access tokens are encrypted JWTs — AddJwtBearer cannot read them and fails with a
// silent 401. No cookies, no session: a pure token resource server (auth.md).
builder.Services.AddAuthentication(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AuthPolicies.PlatformAdmin, policy => policy.RequireRole(AuthPolicies.PlatformAdmin))
    .AddPolicy(AuthPolicies.TenantMember, policy => policy.AddRequirements(new TenantRoleRequirement(TenantRole.Member)))
    .AddPolicy(AuthPolicies.TenantOfficer, policy => policy.AddRequirements(new TenantRoleRequirement(TenantRole.Officer)))
    .AddPolicy(AuthPolicies.TenantOwner, policy => policy.AddRequirements(new TenantRoleRequirement(TenantRole.Owner)));

// Every layer below the controllers lives in AegisScribe.Domain and registers itself. The host
// supplies only what is host-shaped: the DbContext, Identity, and the caller from the token.
builder.Services.AddAegisScribeDomain();

// Separate, because the sync worker wants this without the request stack. Missing credentials are a
// supported state — the gateway degrades and the host still starts (external.md).
builder.Services.AddBlizzardIntegration();

// Per-tenant fairness, distinct from the Blizzard limiter: that is the contractual cap on the whole
// process, this is one community's share of tenant-triggered work.
builder.Services.AddOptions<TenantSyncBudgetOptions>()
    .BindConfiguration(TenantSyncBudgetOptions.SectionName)
    .Validate(
        options => options.IsValid,
        "TenantSyncBudget:CallsPerWindow and Window must both be positive — a non-positive budget " +
        "refuses every tenant-triggered sync rather than removing the limit.")
    .ValidateOnStart();

// Tagged external so it stays out of /health: absent Blizzard credentials is normal locally and must
// not make the API look unready.
builder.Services.AddHealthChecks()
    .AddCheck<BlizzardGatewayHealthCheck>("blizzard", tags: [HealthCheckTags.External]);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddScoped<IAuthorizationHandler, TenantRoleAuthorizationHandler>();
builder.Services.AddScoped<IIdempotencyStore, RedisIdempotencyStore>();

// Where facade and business exceptions become problem+json. Controllers never catch them.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();

// Partitioned by sub, then client_id (a fleet-wide budget), then IP for anonymous requests only —
// never falling back to IP once either claim is present, because a carrier NAT puts a whole city
// behind one address (backend.md → "The API's public edge").
//
// Fixed window, not sliding: SlidingWindowRateLimiter doesn't reliably populate the RetryAfter lease
// metadata when QueueLimit is 0 (dotnet/runtime#131175).
var rateLimits = builder.Configuration.GetSection(RateLimitOptions.SectionName).Get<RateLimitOptions>()
    ?? new RateLimitOptions();

if (!rateLimits.IsValid)
{
    // A non-positive permit limit does not relax the limiter, it rejects everything.
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

    // Stacks on the global limiter below. The endpoint is authenticated, so the IP fallback only
    // catches a misconfiguration.
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

// Non-production, not just non-Development: a public schema is free reconnaissance and there are no
// third-party consumers to serve it to (backend.md).
if (!app.Environment.IsProduction())
{
    app.MapOpenApi().WithDocumentPerVersion();
}

// Before UseHttpsRedirection, so the public scheme is restored from X-Forwarded-Proto first —
// otherwise the plain-HTTP gateway→API hop looks insecure and gets redirected.
//
// KnownNetworks/KnownProxies stay at their loopback defaults: gateway and API share a machine here. A
// multi-host deployment would have to widen them to the gateway's subnet.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
});

// Early, so it wraps the tenant-resolution middleware as well as the controllers.
app.UseExceptionHandler();

app.UseHttpsRedirection();

// Serves the CSS for the connect/authorize sign-in page, the one server-rendered screen in the app.
// Before UseAuthentication so those files never run through the pipeline built for API routes.
app.UseStaticFiles();

app.UseAuthentication();
// After authentication, so the limiter's sub/client_id partitioning sees the token's claims.
app.UseRateLimiter();
// After authentication (needs HttpContext.User) and before authorization (tenant-aware policies read
// the ITenantContext this populates) — tenancy.md.
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseAuthorization();

app.MapControllers();

app.MapDefaultEndpoints();

app.Run();

// Expects a base64-encoded PFX. Real production loading (Key Vault, mounted secret) is a deployment
// decision this app does not make yet.
static X509Certificate2 LoadOidcCertificate(string? base64Pfx)
{
    if (string.IsNullOrEmpty(base64Pfx))
    {
        throw new InvalidOperationException(
            "No OpenIddict certificate configured for a non-Development environment.");
    }

    return X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(base64Pfx), password: null);
}
