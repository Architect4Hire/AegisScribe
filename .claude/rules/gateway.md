---
paths:
  - src/AegisScribe.Gateway/**
  - src/AegisScribe.Web/**
---
# Gateway rules — the YARP BFF

Three publicly addressable deployables, three hostnames, one registrable domain (the sync worker and
migration service are the private rest):

```
app.aegisscribe.com        bff.aegisscribe.com          api.aegisscribe.com
┌─────────────────┐        ┌──────────────────────┐     ┌──────────────────┐
│ AegisScribe.Web │ fetch  │ AegisScribe.Gateway  │ JWT │   ApiService     │
│ serves the      │ ─────► │ YARP BFF             │ ──► │   JWT resource   │
│ Angular bundle  │ cookie │ · owns the session   │     │   server         │
└─────────────────┘        │ · cookie → JWT       │     │   ALSO PUBLIC    │
                           │ · strips client hdrs │     └──────────────────┘
                           └──────────────────────┘              ▲
                                                                 │ curl, Swagger,
                                                                 │ ops access
                                                                 │
                                                   ┌─────────────┴────────┐
                                                   │ AegisScribe.Mobile   │
                                                   │ .NET MAUI            │
                                                   │ OAuth public client  │
                                                   │ code + PKCE, tokens  │
                                                   └──────────────────────┘
```

**The API is publicly addressable, and that is deliberate.** It is its own deployable with its own
hostname, so you can curl production, read its OpenAPI document in non-production, and diagnose an incident without
first working out whether the gateway or the API is at fault. That costs something — see "The API's
public edge" below — and the cost is paid explicitly rather than by pretending the API is hidden.

**The browser still only ever talks to `bff.*`.** Nothing in the SPA calls `api.*` directly. The
public API hostname is for the **mobile app**, for humans and for tooling — not for the front end.

**The mobile app is not a reason to move the SPA off the gateway.** They are different clients with
different capabilities: a native app has no cookie jar shared with a gateway, no origin and no XSS
surface of the kind a SPA has, so tokens are the right answer there and the wrong one in a browser.
"Mobile holds tokens, so the SPA may as well" is the argument this file exists to refuse.

**Cross-origin, same-site.** `app.` and `bff.` are different origins — so CORS applies — but the same
*site*, so the auth cookie survives with `SameSite=Lax`. That distinction is the whole reason this
works; two genuinely different registrable domains would force `SameSite=None`, which Safari's ITP
blocks outright.

## Why this shape

- **The browser gets a cookie.** `HttpOnly`, so JavaScript cannot read it; an XSS in the SPA cannot
  exfiltrate a portable credential. Logout is instant because the session lives server-side.
- **The API gets a JWT.** Stateless, signed, independently verifiable — the same token the mobile app
  presents, so the API has one authorization path rather than one per front end.
- **Three hosts, three release cadences.** The SPA ships without touching the API; the API scales
  independently of the thing serving JavaScript; and the mobile app, which ships on someone else's
  schedule entirely, depends on a versioned contract rather than on a deploy
  (`.claude/rules/api-contract.md`).

Putting the JWT in the browser instead would be a **downgrade**: an XSS steals a token that works from
anywhere until it expires, and revocation needs a blocklist you then have to operate.

## It has to be a project, not the Aspire YARP resource

`Aspire.Hosting.Yarp` gives you a **configuration-driven proxy resource** — routes, clusters,
transforms, all declared in the AppHost. Excellent for pure proxying, and useless here, because a BFF
needs *code*: cookie authentication, a session store, the token exchange, header sanitisation.

So the gateway is an ASP.NET Core project referencing **`Yarp.ReverseProxy`** (2.3.0, stable),
registered with `AddProject<...>` like any other service. Don't reach for `AddYarp` and then try to
bolt auth onto it.

## AppHost wiring

```csharp
var api = builder.AddProject<Projects.AegisScribe_ApiService>("api")
                 .WithReference(db).WithReference(cache)
                 .WithExternalHttpEndpoints();    // api.*  — ops access, not the browser's path

var gateway = builder.AddProject<Projects.AegisScribe_Gateway>("gateway")
                     .WithReference(api).WithReference(cache)
                     .WaitFor(api)
                     .WithExternalHttpEndpoints();    // bff.* — the browser's only door

var web = builder.AddProject<Projects.AegisScribe_Web>("web")
                 .WithReference(gateway)              // for the injected BFF URL
                 .WithExternalHttpEndpoints();        // app.*
```

**The sync worker and migration service stay internal.** They have no HTTP surface anyone should
reach. The API is public by choice, and that choice comes with the obligations in "The API's public
edge" below — it is not free.

Destinations resolve through service discovery, never a literal address:

```csharp
builder.Services.AddReverseProxy()
       .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
       .AddServiceDiscoveryDestinationResolver();
```

```jsonc
"ReverseProxy": {
  "Routes":   { "api": { "ClusterId": "api", "Match": { "Path": "/api/{**catch-all}" } } },
  "Clusters": { "api": { "Destinations": { "api": { "Address": "http://api" } } } }
}
```

## The web host

`AegisScribe.Web` is deliberately thin: it serves the built Angular bundle and nothing else.

- `UseStaticFiles()` plus `MapFallbackToFile("index.html")` for client-side routing.
- It injects the **gateway's base URL** into the app at runtime — an `/appsettings.json` endpoint or a
  templated `index.html`. The SPA must not have the gateway URL baked in at build time, or you cannot
  promote the same artifact between environments.
- **No API calls, no session, no auth.** It is a file server with one config endpoint. If logic starts
  accumulating here, it belongs in the gateway.

### Development vs production

- **Development:** Aspire runs the Angular dev server (`AddJavaScriptApp`) for hot reload, and the SPA
  calls the gateway directly. `localhost:4200` and the gateway's port are different origins but the
  **same site**, so `SameSite=Lax` behaves exactly as it will in production — the dev setup does not
  paper over a cookie problem you would then meet on deploy.
- **Production:** `AegisScribe.Web` serves the built bundle at `app.*`. No dev server, no HMR.

The gateway's CORS origin therefore comes from configuration and differs per environment. That is the
only thing that changes between the two.

## Cookies across subdomains

```csharp
options.Cookie.HttpOnly     = true;
options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
options.Cookie.SameSite     = SameSiteMode.Lax;          // NOT Strict — see below
options.Cookie.Domain       = ".aegisscribe.com";        // so app. can send it to bff.
options.Cookie.Name         = "__Secure-aegisscribe";    // NOT __Host- — that prefix forbids Domain
options.SlidingExpiration   = true;
```

Four things here are consequences of the split, and each is a trap if you copy a same-origin example:

- **`SameSite=Lax`, not `Strict`.** Strict would not send the cookie on a top-level navigation
  arriving from another site, which breaks returning from an external link. Lax is correct for
  same-site subdomains, and still blocks the cookie on genuinely cross-site POSTs.
- **`__Secure-` prefix, not `__Host-`.** `__Host-` forbids a `Domain` attribute, and we need one.
- **`Domain=.aegisscribe.com` sends the cookie to every subdomain — including `api.*`.** The API
  ignores cookies entirely (it authenticates bearer tokens only), so this is harmless there, but it
  means **no untrusted content on any `aegisscribe.com` subdomain, ever**.
- **The cookie reaching every subdomain is why the API must not accept cookie auth.** No user-content
  hosting, no third-party preview domains, no staging subdomain running someone else's code. That is a
  security rule about DNS, not just about code.
- **CSRF still needs a second lock.** `SameSite=Lax` blocks cross-site POSTs, and CORS restricts who
  may read responses — but require a custom header (`X-Requested-With`) on state-changing requests
  too. A custom header forces a CORS preflight, and the preflight only succeeds for our allowed
  origin.

## CORS — explicit, credentialed, no wildcards

```csharp
builder.Services.AddCors(o => o.AddPolicy("spa", p => p
    .WithOrigins(builder.Configuration["Spa:Origin"]!)   // exact origin, from config
    .AllowCredentials()
    .AllowAnyHeader()
    .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")));
```

`AllowAnyOrigin()` and `AllowCredentials()` are mutually exclusive by specification, and any code that
appears to do both is either broken or reflecting the caller's origin — which is the same as having no
CORS at all. The origin comes from configuration, never a literal, so environments differ by config.

## The gateway is an OAuth confidential client

This is the part that changed when a mobile app entered the picture, and the change is a
simplification: the gateway no longer calls a bespoke, gateway-only mint endpoint. It runs the **same
authorization code + PKCE flow the mobile app runs**, as the registered client `aegisscribe-bff` —
the difference being that it is a server, so it also holds a client secret, and it keeps the tokens
instead of handing them to the browser.

That is the textbook BFF: one protocol, two client types, and the API with a single authorization
path to reason about rather than one code path per front end.

1. The browser hits the gateway's `/auth/login`. **This route is handled by the gateway, not
   proxied** — login and logout are the gateway's own concerns.
2. The gateway redirects to the API's `connect/authorize` with PKCE, and handles the callback.
3. It exchanges the code at `connect/token` — **server-to-server, with its client secret** — and gets
   an access token and a refresh token.
4. Both go into the **server-side session**; the browser gets only a session cookie.
5. On every proxied request the gateway pulls the access token from the session and sets
   `Authorization: Bearer`.
6. Before expiry it refreshes **server-side**, using the refresh token. The browser is never involved
   and never sees a token.

The client secret is an **Aspire parameter** (`AddParameter(..., secret: true)`), never a literal.

Sessions live in **Redis** via an `ITicketStore`, so the cookie carries only a session id:

- the cookie stays small — tokens plus claims can approach the 4 KB cookie limit;
- **logout is instant**, because the server drops the session rather than waiting for a token to
  expire — and it revokes the refresh token at `connect/revoke` on the way out, so the session ending
  is not merely local;
- gateway instances are interchangeable, so it scales horizontally.

Hold the access token for its lifetime rather than requesting one per proxied request, which would
turn every page load into a burst of token requests. **Refresh is single-flight** for the same reason
it is on mobile: a burst of proxied requests arriving as the token expires must produce one refresh,
not twenty.

## Header sanitisation — the one that bites

**Strip client-supplied `Authorization` before adding our own.** Without this, a caller sends their own
bearer token and the gateway forwards it — the API can no longer tell a gateway-minted token from an
attacker-supplied one, and the whole boundary is decorative.

```csharp
transforms.AddRequestTransform(async ctx =>
{
    ctx.ProxyRequest.Headers.Remove("Authorization");        // whatever the client sent
    ctx.ProxyRequest.Headers.Remove("X-Forwarded-For");
    ctx.ProxyRequest.Headers.Remove("X-Forwarded-Proto");
    ctx.ProxyRequest.Headers.Remove("X-Forwarded-Host");

    var token = await sessions.GetAccessTokenAsync(ctx.HttpContext);
    if (token is not null)
        ctx.ProxyRequest.Headers.Authorization = new("Bearer", token);
});
```

The rule in one line: **anything the API trusts, the gateway sets. Anything the client sent that the
API might trust, the gateway removes.**

Configure `ForwardedHeaders` on the **API** to accept those headers only from the gateway's network, so
it sees the real scheme and host without trusting arbitrary callers.

## Anonymous routes still work

Character lookup is the app's front door and stays anonymous. With no session the gateway proxies
without an `Authorization` header and the API's `[AllowAnonymous]` routes serve it. **Absence of a
token is a normal state**, not an error — don't make the gateway demand a session before proxying.

## What the gateway must not do

- **No business logic.** It authenticates, sanitises and proxies. A gateway that starts making domain
  decisions becomes a second place to look for them.
- **No database access.** Redis for sessions, nothing else. No `DbContext`, ever.
- **No tenant resolution.** The tenant comes from the route and is resolved by the API against
  membership. The gateway forwards the path unchanged and forms no opinion — see
  `.claude/rules/tenancy.md`.
- **No response body rewriting.** If a response needs shaping, that is the API's job.

## Testing

- **The strip test:** a request carrying a forged `Authorization` header reaches the API carrying the
  gateway's token, not the forged one. The single most important test in the project's auth surface.
- **No token in the browser:** assert no response from the gateway — body, header or cookie — contains
  a JWT. Grep for `eyJ`, the base64 prefix every JWT starts with.
- **CORS is closed:** a preflight from an origin that isn't the configured SPA origin is rejected, and
  the response never reflects the caller's origin back.
- **Cookie attributes:** the `Set-Cookie` carries `HttpOnly`, `Secure`, `SameSite=Lax`,
  `Domain=.aegisscribe.com` and the `__Secure-` prefix. Assert on the header string; these regress
  silently.
- **Anonymous passthrough:** an unauthenticated request to a public route succeeds with no
  `Authorization` header forwarded.
- **Logout is instant:** after logout the session is gone from Redis and the next proxied request is
  unauthenticated, without waiting for token expiry.
- **Token refresh is invisible:** a request spanning token expiry succeeds without a browser round
  trip.
