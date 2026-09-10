---
paths:
  - src/AegisScribe.ApiService/Auth/**
  - src/AegisScribe.ApiService/Tenancy/**
  - src/AegisScribe.ApiService/Managers/Models/Identity/**
  - src/AegisScribe.Gateway/**
  - src/web/src/app/auth/**
  - src/web/src/app/core/**
---
# Auth rules — Identity for *who*, tenant membership for *what*

Two separate questions, two separate mechanisms, and conflating them is the mistake this file exists
to prevent:

- **Who are you?** ASP.NET Core Identity, local accounts. One account per person, platform-wide.
- **What may you do?** **Tenant membership.** A domain entity, per community, resolved per request.

## Two front doors, one resource server

There are two kinds of client and they authenticate differently. The API does not care which one it
is talking to — it validates a bearer token and nothing else.

| Client | Holds | Why that credential |
|---|---|---|
| **Browser** (Angular at `app.*`) | `HttpOnly` session **cookie** against the gateway | JavaScript can't read it, so an XSS can't exfiltrate a portable credential. Logout is instant, because the session is server-side. |
| **Mobile** (.NET MAUI) | **access + refresh token**, in platform secure storage | A native app has no cookie jar shared with a gateway and no origin. It is a first-class OAuth public client. |
| Gateway → API, Mobile → API | short-lived **JWT**, `Authorization: Bearer` | The same token shape from both doors, so the API has exactly one authorization path to reason about and test. |

**The browser still never sees a token.** The gateway holds the tokens in its Redis session and
injects the access token on the proxied request; the SPA has no token, no refresh timer and no
`Authorization` header built in TypeScript. Mobile going token-first does **not** license the SPA to
do the same — see `.claude/rules/gateway.md`.

The API is a **pure token resource server**. No cookie authentication, no login form, no session, no
antiforgery. That is unchanged by mobile arriving; what changed is that the API now issues tokens as
well as validating them.

Local accounts only — no external identity provider, no Battle.net OAuth (see CLAUDE.md → Scope).
Microsoft's own framing puts this squarely in the built-in Identity case: first-party app, no consent,
no federation.

## Identity roles are platform-level only

There is essentially one: **`PlatformAdmin`** — us, the operators. That's it.

Identity roles cannot express "officer in Community A, member in Community B", which is the normal
case for anyone in more than one guild. **Do not add `Officer` or `Member` as Identity roles.** If you
see `[Authorize(Roles = "Officer")]` anywhere, it is a bug that will grant that person officer rights
in every community they belong to.

## Tenant membership is the authorization model

```csharp
public class TenantMembership
{
    public Guid   TenantId { get; set; }
    public string UserId   { get; set; } = null!;
    public TenantRole Role { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
}

public enum TenantRole { Member = 0, Officer = 10, Owner = 20 }
```

The enum is **ordered on purpose**: `Owner` implies `Officer` implies `Member`, encoded once in the
requirement handler so no endpoint ever lists three roles.

```csharp
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("TenantMember",  p => p.AddRequirements(new TenantRoleRequirement(TenantRole.Member)))
    .AddPolicy("TenantOfficer", p => p.AddRequirements(new TenantRoleRequirement(TenantRole.Officer)))
    .AddPolicy("TenantOwner",   p => p.AddRequirements(new TenantRoleRequirement(TenantRole.Owner)))
    .AddPolicy("PlatformAdmin", p => p.RequireRole("PlatformAdmin"));
```

`TenantRoleRequirement`'s handler reads the resolved tenant from `ITenantContext` and the caller's
membership, and satisfies the requirement when `membership.Role >= required`. **It must fail closed**:
no resolved tenant, or no membership, is a failure — never a default to `Member`.

Membership itself is tenant-scoped data and follows every rule in `.claude/rules/tenancy.md`.

## One user, one context

```csharp
public class ApplicationUser : IdentityUser
{
    public string? DisplayName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? LastTenantId { get; set; }   // a UI convenience, never an authorization input
}

public class AegisScribeDbContext : IdentityDbContext<ApplicationUser, IdentityRole, string>
{
    // global reference DbSets and tenant-scoped DbSets both live here
}
```

`LastTenantId` remembers which community to open on sign-in. **It is never read by an authorization
check** — the tenant comes from the route, always.

## OpenIddict issues the tokens

**The trap, stated first, because it costs a day otherwise:** the bearer tokens `MapIdentityApi`
issues are **not JWTs**. They are opaque, Data-Protection-encrypted blobs in a proprietary format,
validatable only by the process that created them. If you find yourself calling `/login?useCookies=false`
and expecting a JWT back, stop.

Real tokens come from **OpenIddict**, hosted inside the API. It is not a hand-written issuer, and
writing one here is a regression — see "Why not hand-rolled" below.

```
OpenIddict                  7.7.0   (verify — 7.7.0 shipped 2026-09-06; this moves fast)
OpenIddict.AspNetCore
OpenIddict.EntityFrameworkCore
```

`AegisScribeDbContext` gains OpenIddict's four tables — applications, authorizations, scopes, tokens:

```csharp
builder.Services.AddDbContext<AegisScribeDbContext>(options =>
{
    options.UseSqlServer(...);
    options.UseOpenIddict();          // ← registers the entity sets
});

builder.Services.AddOpenIddict()
    .AddCore(o => o.UseEntityFrameworkCore().UseDbContext<AegisScribeDbContext>())
    .AddServer(o =>
    {
        o.SetTokenEndpointUris("connect/token")
         .SetAuthorizationEndpointUris("connect/authorize")
         .SetEndSessionEndpointUris("connect/logout")
         .SetRevocationEndpointUris("connect/revoke");

        o.AllowAuthorizationCodeFlow().RequireProofKeyForCodeExchange()
         .AllowRefreshTokenFlow();

        o.SetAccessTokenLifetime(TimeSpan.FromMinutes(15));

        o.AddEncryptionKey(...).AddSigningKey(...);   // dev: AddDevelopmentEncryptionCertificate()
        o.UseAspNetCore().EnableTokenEndpointPassthrough()
                         .EnableAuthorizationEndpointPassthrough();
    })
    .AddValidation(o => { o.UseLocalServer(); o.UseAspNetCore(); });
```

### The second trap: do not validate with `AddJwtBearer`

OpenIddict's access tokens are JWTs, but they are **encrypted (JWE) by default**. `AddJwtBearer`
cannot read them, and the failure looks like a generic 401 with nothing useful in the log.

Because the issuer and the resource server are the same process here, the answer is
`AddValidation(o => o.UseLocalServer())` — it shares the server's keys directly, no JWKS fetch, no
decryption puzzle. **Do not** reach for `DisableAccessTokenEncryption()` to make `AddJwtBearer` work;
that trades a real protection for a familiar API. The only reason to disable encryption is a
*separate* resource server that can't do JWE, and there isn't one.

The auth scheme constant is `OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme`, not
`JwtBearerDefaults`.

### Three registered clients, and only three

Seeded once, in the migration service, and **first-party only** — there is no client registration UI
and no third-party developer programme.

| Client | Type | Flow | Secret | Why |
|---|---|---|---|---|
| `aegisscribe-bff` | confidential, web | code + PKCE, refresh | yes — an Aspire parameter | The gateway, acting for the browser. It can hold a secret because it's a server. |
| `aegisscribe-mobile` | **public**, native | code + PKCE, refresh | **no** | A shipped app cannot keep a secret. PKCE is what replaces it. |
| `aegisscribe-ops` | confidential | client credentials | yes | Service-to-service only — the sync worker calling the API. Never a user's token. |

The mobile client is registered as a **native** application, which matters mechanically:

```csharp
new OpenIddictApplicationDescriptor
{
    ClientId         = "aegisscribe-mobile",
    ClientType       = ClientTypes.Public,          // no secret
    ApplicationType  = ApplicationTypes.Native,     // RFC 8252 redirect matching
    RedirectUris     = { new Uri("aegisscribe://auth/callback") },
    PostLogoutRedirectUris = { new Uri("aegisscribe://auth/signout") },
    Permissions = { /* endpoints, grant types, scopes — enumerate explicitly */ },
}
```

`ApplicationTypes.Native` relaxes redirect-URI comparison per RFC 8252 so a loopback redirect may use
a runtime-chosen port. A **public client with no PKCE is a Blocker** — without a secret, PKCE is the
only thing binding the authorization code to the app that requested it.

**Never put a client secret in the MAUI app.** Not in code, not in a config file, not obfuscated.
Anything shipped to a device is public; that is what `ClientTypes.Public` means and why the mobile
client has no secret to leak. See `.claude/rules/mobile.md`.

### Refresh tokens: the defaults are already right

OpenIddict's defaults, which this repo keeps:

| Behaviour | Default | Keep it because |
|---|---|---|
| Rolling refresh tokens (rotation) | **on** (`DisableRollingRefreshTokens = false`) | Each refresh redeems the old token and issues a new one. **Disabling this is a Blocker.** |
| `RefreshTokenLifetime` | 14 days | A mobile user who opens the app fortnightly stays signed in; one who vanishes is cut off. |
| Sliding expiration | on | The 14 days runs from last use, not from first sign-in. |
| `RefreshTokenReuseLeeway` | 30 seconds | See below — do not set this to zero. |

**The leeway is not a bug, and someone will try to remove it.** A redeemed refresh token keeps
working for 30 seconds. That window exists because mobile networks produce genuine concurrent
refreshes — two queued requests both 401, both refresh, and without leeway the second one logs the
user out for no reason. Zero leeway trades a real, common bug for a marginal security gain.

Reuse *after* the leeway means a stolen token: OpenIddict rejects it. Treat repeated post-leeway
reuse as a signal and revoke that authorization's whole token chain.

### Token storage and pruning

OpenIddict writes a database row per issued token, which is what makes revocation possible at all —
"log this device out" is a real operation rather than "wait for expiry". The cost is that the tokens
table grows forever unless something prunes it. **The sync worker owns the pruning job**; a missing
one is a slow-motion outage, not a tidiness issue.

### Why not hand-rolled

An earlier version of this design hand-wrote the issuer. That was defensible when the only client was
a gateway on the same team. It stopped being defensible when a public client entered the picture:
refresh-token rotation with replay detection, PKCE verification, authorization-code single-use, and
revocation are each easy to write *almost* correctly, and "almost" is indistinguishable from correct
in testing. If you are writing token-minting code by hand in this repo, you have taken a wrong turn.

### What goes in the token

`sub` (the user id), the standard registered claims, and `PlatformAdmin` if they have it. **That is
all.**

**No tenant claims** — and with mobile this matters more, not less. A refresh token lives 14 days; a
tenant claim minted at sign-in could be a fortnight stale, so an officer demoted on Monday would keep
officer rights on their phone until Friday. Membership is resolved per request against the database,
so a demotion takes effect on the next call. It costs one cached lookup and removes a whole category
of stale-authorization bug. See `.claude/rules/tenancy.md`.

### Signing keys

Asymmetric, RS256 or ES256. Anyone who can validate a symmetric token can also forge one; with an
asymmetric key the private half never leaves the issuer. `AddDevelopmentSigningCertificate()` is for
local development **only** — in any deployed environment the keys come from configuration, and
committing a certificate is a Blocker. OpenIddict handles `kid`, JWKS publication and rotation; a
`kid` must carry nothing tenant-shaped or user-shaped.

### Logout means revoke

Three different operations, and conflating them is how a "signed out" device keeps working:

- **Browser** — the gateway drops the Redis session. Instant, because the session is server-side.
- **Mobile** — the app calls `connect/revoke` with its refresh token, *then* clears local storage.
  Clearing storage alone leaves a live refresh token in the database that a device backup could
  restore. Revoke first; treat a failed revoke as a retry, not a success.
- **"Sign out everywhere"** — revoke the user's authorizations, which invalidates every refresh token
  across devices. Access tokens already issued stay valid until they expire, which is why they are 15
  minutes and not 15 hours.

## Identity endpoints, and what they don't give you

`AddIdentityApiEndpoints` still provides registration, password management and the user store.
Identity remains the **user store**; OpenIddict is the **protocol layer** on top of it. Two things
are ours to write:

1. **The authorization endpoint's sign-in page** — the `connect/authorize` handler resolves the
   current Identity user and issues the OpenIddict principal. Both clients go through it: the mobile
   app in a system browser, the gateway by redirecting the browser to it and handling the callback.
   Only the `connect/token` exchange is server-to-server.
2. **Role and membership management** — invitations, join requests, role changes, removal, all under
   `TenantOfficer`/`TenantOwner` and all audited.

The token issuer is **not** on this list any more. OpenIddict owns it.

## Membership lifecycle

The paths a person takes into and out of a community, all of them audited:

- **Invite** — an officer creates a single-use, expiring invitation token for a role; the invitee
  accepts while signed in. The token grants membership in exactly one tenant at exactly one role.
- **Request to join** — a user asks; an officer approves or declines. A tenant chooses which of these
  two doors is open.
- **Role change** — `Officer` may promote to `Member`/`Officer`; only `Owner` may create another
  `Owner` or demote an `Officer`.
- **Removal** — an officer removes a member; the member's `RosterEntry` rows in that tenant go with
  it. Their global character data does not.
- **Last owner** — a tenant must always have at least one `Owner`. The demote and remove paths both
  refuse the last one. This is a domain rule and lives in Business.

Every one of these writes an `AuditLog` row: who, what, which tenant, when, and the before/after role.
Once officers can act on other people's data, "who did this" stops being optional.

## Resource authorization is a domain rule

"Can this user edit this roster entry?" is not a policy — it depends on data. That check lives in
**Business**, throws a domain exception, and the global handler maps it to 403. Don't reach for
`IAuthorizationService` inside a facade.

The distinction: **policies answer "what rank are you here"; Business answers "is this yours"**.

## .NET 10 changed one thing that dates older tutorials

Cookie authentication no longer 302-redirects failed API requests to a login page — API endpoints now
correctly return a bare 401/403. If you find a blog-sourced `OnRedirectToLogin` workaround in this
codebase, it's solving a problem the framework fixed; delete it.

(.NET 10 also added native passkey support to Identity. It is **not** wired into `MapIdentityApi`, so
it would need its own endpoints. Out of bounds unless asked for.)

## Angular side

The SPA is served from `app.aegisscribe.com` and calls **`bff.aegisscribe.com`** — cross-origin,
same-site. It never calls `api.*`; that hostname exists for the mobile app and for ops.

- **Functional interceptor**, registered with `provideHttpClient(withInterceptors([...]))` — not the
  class-based `HTTP_INTERCEPTORS` provider.
- Every API request carries **`withCredentials: true`**, and here it is genuinely load-bearing: the
  request is cross-origin, so without it the browser sends no cookie and every call is anonymous. Set
  it centrally in the interceptor, never per call site.
- Add **`X-Requested-With`** on state-changing requests. It forces a CORS preflight, which only our
  configured origin passes — the second CSRF lock behind `SameSite=Lax`.
- **The gateway's base URL comes from runtime config**, fetched from the web host at startup, not baked
  in at build time. The same bundle has to promote between environments.
- **The SPA never handles a token.** There is no token in `localStorage`, no `Authorization` header
  built in TypeScript, no refresh timer. If you are writing any of those, the design has been
  misunderstood — the gateway owns all of it. **The mobile app doing exactly those things is not a
  precedent**: it has no gateway and no cookie, and the browser has both. Copying the MAUI auth code
  into TypeScript would undo the reason the gateway exists.
- **Functional guards** (`CanActivateFn` + `inject()`), no `@Injectable` guard classes. Two of them: an
  auth guard, and a **tenant guard** that confirms the route's tenant slug is one the user is a member
  of before the route activates.
- The **tenant slug lives in the route**, mirroring the API. `/t/:tenantSlug/roster`. Services build
  URLs from the active route's tenant, never from a stored variable that can drift.
- A 401 means the session ended: clear client auth state and route to login. A **404 on a tenant route
  means "not yours"** — route to the tenant picker, don't show an error.
- The signed-in user's memberships come from `GET /api/v1/me` — the tenants they belong to and their role
  in each. The client **displays** based on role; it never **enforces**. Every protected route has a
  server-side policy behind it; hiding a button is cosmetics.
