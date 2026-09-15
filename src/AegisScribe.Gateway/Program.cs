using System.Net.Http.Headers;
using AegisScribe.Gateway.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddRedisDistributedCache("cache");

builder.Services.AddSingleton<RefreshCoordinator>();

// Its own singleton rather than constructed inline for the cookie options, so RefreshCoordinator can
// read and write it directly — bypassing HttpContext.AuthenticateAsync's per-request cache, which
// would otherwise make its post-lock re-check a no-op.
builder.Services.AddSingleton<ITicketStore, DistributedCacheTicketStore>();

// Server-to-server only (token refresh, connect/revoke on logout), resolved through service discovery
// like every other inter-service call (aspire.md).
builder.Services.AddHttpClient(GatewayAuthDefaults.ApiBackchannelClient, client =>
{
    client.BaseAddress = new Uri("http://api");
});

// The cookie options need a DI service, which the inline AddCookie(...) delegate below cannot take.
// Configure<T> is the documented way around that.
builder.Services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
    .Configure<ITicketStore>((options, store) =>
    {
        options.SessionStore = store;
    });

// The browser is redirected here as a top-level navigation, so it has to be an address the browser
// itself can reach — the same service-discovery value that seeds this client's RedirectUris.
var apiBase = builder.Configuration["services:api:https:0"] ?? builder.Configuration["services:api:http:0"]
    ?? throw new InvalidOperationException("API endpoint is not configured for the OIDC client.");

// The SPA origin, from config — never a literal (gateway.md → "CORS"). Sourced from AegisScribe.Web,
// the only SPA-serving resource wired into the AppHost today.
var spaOrigin = builder.Configuration["services:web:https:0"] ?? builder.Configuration["services:web:http:0"]
    ?? throw new InvalidOperationException("SPA endpoint is not configured for the CORS policy.");

builder.Services.AddCors(options => options.AddPolicy("spa", policy => policy
    .WithOrigins(spaOrigin)
    .AllowCredentials()
    .AllowAnyHeader()
    .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")));

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        // gateway.md → "Cookies across subdomains": every attribute here is a direct consequence
        // of app.*/bff.*/api.* being different origins on one registrable domain.
        options.Cookie.Name = GatewayAuthDefaults.CookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax; // not Strict — see gateway.md

        // Domain=.aegisscribe.com is what lets app.* hand this cookie to bff.* in a real environment,
        // but a browser only accepts a Domain attribute matching the current host. Under `aspire run`
        // the host is bare "localhost", so the browser discards the cookie outright (RFC 6265) rather
        // than scoping it oddly — sign-in "succeeds" and every later request is anonymous. A host-only
        // cookie is what a same-host local setup needs, so this is the one environment-conditional
        // cookie attribute; the rest above are not.
        if (!builder.Environment.IsDevelopment())
        {
            options.Cookie.Domain = ".aegisscribe.com"; // NOT __Host- eligible; see the name prefix above
        }

        options.SlidingExpiration = true;
    })
    .AddOpenIdConnect(options =>
    {
        options.Authority = apiBase;
        options.ClientId = GatewayAuthDefaults.ClientId;
        options.ClientSecret
            = builder.Configuration["Oidc:BffClientSecret"]; // an Aspire parameter, never a literal
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.UsePkce = true; // ASP.NET Core default; stated explicitly per gateway.md
        options.SaveTokens = true; // access/refresh/id tokens land in the ticket's properties
        options.GetClaimsFromUserInfoEndpoint = false; // no userinfo endpoint exists
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("offline_access"); // what makes OpenIddict issue a refresh token at all
        options.CallbackPath = "/auth/callback";
    });

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver()
    .AddTransforms(transformBuilderContext =>
    {
        transformBuilderContext.AddRequestTransform(async transformContext =>
        {
            // Strip whatever the caller sent — the single most important control in the auth surface
            // (gateway.md → "Header sanitisation"). Unconditional, not "overwrite when we have our
            // own token": with no session accessToken below is null and nothing else would touch this
            // header, letting an anonymous caller's forged Authorization reach the API untouched.
            transformContext.ProxyRequest.Headers.Remove("Authorization");

            // X-Forwarded-For/Proto/Host need no code here — YARP strips and replaces them by default
            // on every proxied request.

            var refreshCoordinator = transformContext.HttpContext.RequestServices
                .GetRequiredService<RefreshCoordinator>();
            var accessToken = await refreshCoordinator.GetValidAccessTokenAsync(transformContext.HttpContext);

            // Absence of a token is a normal state (anonymous character lookup, etc.) — gateway.md.
            if (accessToken is not null)
            {
                transformContext.ProxyRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }
        });
    });

var app = builder.Build();

app.MapDefaultEndpoints();

// Ahead of both cookie auth and the proxy, so a rejected preflight never reaches either.
app.UseCors("spa");

app.UseAuthentication();

// Handled by the gateway itself, not proxied — login and logout are its own concerns (gateway.md).
app.MapGet("/auth/login", (string? returnUrl) =>
{
    // returnUrl is SPA-relative and only ever meant to resolve against the SPA's origin; left bare it
    // resolves against the gateway, which 404s once the browser lands back after sign-in. Requiring a
    // single leading '/' (never '//', never a scheme) also keeps this untrusted query parameter from
    // becoming an open redirect.
    var isLocalPath = returnUrl is { Length: > 0 } path
        && path[0] == '/'
        && (path.Length == 1 || path[1] != '/');

    return Results.Challenge(
        new AuthenticationProperties { RedirectUri = spaOrigin + (isLocalPath ? returnUrl : "/") },
        [OpenIdConnectDefaults.AuthenticationScheme]);
});

app.MapPost("/auth/logout", async (HttpContext httpContext, IHttpClientFactory httpClientFactory) =>
{
    var authResult = await httpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    var refreshToken = authResult.Properties?.GetTokenValue("refresh_token");

    if (refreshToken is not null)
    {
        var bffSecret = builder.Configuration["Oidc:BffClientSecret"];
        var client = httpClientFactory.CreateClient(GatewayAuthDefaults.ApiBackchannelClient);

        // Revoke first, then clear — a failed revoke is a retry, not a success (auth.md's "Logout
        // means revoke"). This best-effort call surfaces failures via the response status only.
        await client.PostAsync("connect/revoke", new FormUrlEncodedContent(
        [
            new("token", refreshToken),
            new("token_type_hint", "refresh_token"),
            new("client_id", GatewayAuthDefaults.ClientId),
            new("client_secret", bffSecret),
        ]));
    }

    // Drops the session from Redis — logout is instant, not "expires eventually" (gateway.md).
    await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.NoContent();
});

app.MapReverseProxy();

app.Run();
