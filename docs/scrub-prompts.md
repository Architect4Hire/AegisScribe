# AegisScribe — SCRUB Microprompts

Prompts for driving Claude Code on AegisScribe, wired to the rules, skills, references and subagents
in your `.claude/` folder.

**These are deliberately small.** One prompt, one seam. Most produce a handful of files and one green
test run. That trades more round-trips for a much shorter leash: you approve a plan you can actually
read, and a wrong turn costs one prompt instead of a phase.

Two parts:

- **Part 1 — Build sequence:** 18 phases, 127 microprompts, run in order.
- **Part 2 — Operational templates:** reusable prompts for the recurring, high-stakes moments the
  agent won't self-guard.

## The reusable SCRUB skeleton

```
SCOPE:        what to build/change + which part of the repo it touches
CONSTRAINT:   the rules to honor (stack, conventions, plan-first)
RESTRICTION:  explicit "do NOT" guardrails
UTILIZATION:  which skills / subagents / tools to use
BEHAVIOR:     how to proceed — plan, approve, small steps, test, report
```

## How to use these

- Run Part 1 **in order**, one at a time. Don't paste a whole phase at once.
- Each assumes `CLAUDE.md`, `.claude/` and `design/aegisscribe-armory.html` are in place.
- Every prompt plans first and waits for approval. Reading the plan is the biggest quality lever you
  have.
- `/clear` between phases. Rules and skills reload on their own.
- **Phases 1B and 2 are the ones to be fussy about.** The edge is the security boundary and tenancy is
  the invariant everything else sits on — both fail silently when they fail.
- **Phase 13B builds the mobile app last, on purpose.** It consumes a contract, and a contract that is
  still moving means a client rewritten daily. Everything before it is what makes the app cheap.

### Credentials, and when you need them

| From | You need |
|---|---|
| Phase 0 | nothing |
| Phase 6 | Blizzard client id/secret — https://develop.battle.net |
| Phase 10 | a Discord webhook URL on a test server |
| Phase 11 | WarcraftLogs client id/secret — https://www.warcraftlogs.com/api/clients |
| Phase 12 | Azure AI Foundry, or Foundry Local for offline |

Everything before Phase 6 runs on seeded data in seeded tenants.

### Checkpoints — where to stop and actually look

| After | You can |
|---|---|
| **0.6** | `aspire run` and see every resource healthy |
| **1B.4d** | See the sign-in form in the app's own colours, not the OpenIddict default |
| **1B.9** | Prove the edge: forged tokens stripped, no JWT in the browser |
| **2.9** | Prove isolation: two tenants, and B cannot see A |
| **4.6** | `curl` a character and get a ServiceModel |
| **5.6b** | Register a new account from the landing page and sign in |
| **5.6c** | Create a community in the browser and land in its own chrome |
| **5.7** | Open the browser and use the armory |
| **6.4** | Watch a never-synced character arrive from Blizzard |
| **7.5** | Run a real roster with your own ranks |
| **7.5b** | Claim a character and see it marked yours, on the roster and the profile |
| **8.3b** | Join a community from an invite link, in a second browser |
| **8.5** | Take a brand-new community from empty to a working roster |
| **9.7** | Sign up for a raid, as the character that's actually yours |
| **10.5** | Get a Discord message |
| **13.3** | Schedule six weeks of raids by typing a sentence |
| **13B.3** | Sign in on a real phone |
| **13B.7** | Run your guild from the app |
| **14.5** | Ship |

---

# Part 1 — Build sequence

## Phase 0 — Foundation

### 0.1 Solution skeleton - done
```
SCOPE: Create the Aspire 13 / .NET 10 solution with empty projects only: AegisScribe.AppHost,
.ServiceDefaults, .ApiService, .Domain (class library), .SyncWorker, .MigrationService, .Tests under
src/, all in the solution file. ApiService references Domain. No resources, no domain code, no
endpoints.
CONSTRAINT: .claude/rules/aspire.md, backend.md → "Two projects, one direction". Verify template names
and CLI syntax against https://aspire.dev.
RESTRICTION: Do NOT add resources, packages, or code beyond what the templates generate. Domain must
NOT reference ApiService — the reference runs one way only, from each host to Domain.
UTILIZATION: aspire-init skill.
BEHAVIOR: Show me the exact commands before running them. Then confirm `dotnet build` succeeds.
```

### 0.2 SQL Server and Redis in the AppHost - done
```
SCOPE: Declare SQL Server (database "aegisscribedb") and Redis in the AppHost, with a secret password
parameter, a persistent container lifetime, and a data volume.
CONSTRAINT: .claude/rules/aspire.md.
RESTRICTION: The SQL Server image tag MUST be a 2025 tag. The default 2022 image has no VECTOR type
and every AI feature later depends on one. Do NOT reference these from any project yet.
UTILIZATION: add-aspire-resource skill.
BEHAVIOR: Plan the resource declarations, wait for approval, implement, and show me `aspire run`
bringing both containers up healthy.
```

### 0.3 Migration service - done
```
SCOPE: Implement AegisScribe.MigrationService as a worker that applies EF migrations once through an
execution strategy and exits. Wire it in the AppHost with WithReference(db) and WaitFor(db). It
references AegisScribe.Domain (where the DbContext will live), never the API.
CONSTRAINT: .claude/rules/backend.md — the execution-strategy wrapper is required, not optional.
RESTRICTION: No DbContext exists yet — this step is the host and the loop only. Do NOT create one.
UTILIZATION: Plan mode.
BEHAVIOR: Show me the loop before writing. Confirm it starts and exits cleanly.
```

### 0.4 Service defaults and health - done
```
SCOPE: Confirm ServiceDefaults wires OpenTelemetry, health checks, resilience and service discovery,
and that every project calls AddServiceDefaults(). Add /health and /alive to the API.
CONSTRAINT: .claude/rules/aspire.md.
RESTRICTION: Do NOT add custom telemetry or exporters yet.
BEHAVIOR: Report what the dashboard shows for each resource.
```

### 0.5 Angular app - done
```
SCOPE: Scaffold the Angular app in src/web (standalone components, strict TypeScript) and register it
in the AppHost so Aspire launches it and injects the API base URL.
CONSTRAINT: .claude/rules/frontend.md and aspire.md. Verify the AddJavaScriptApp API at aspire.dev.
RESTRICTION: Do NOT run `ng serve` by hand. Do NOT hardcode the API URL. No components yet. This is the
DEV dev-server registration only — the production host, AegisScribe.Web, arrives in 1B.3.
BEHAVIOR: Plan the registration and the config read, wait for approval, then show me the app served
by Aspire.
```

### 0.6 First full run - done
```
SCOPE: Bring the whole system up and confirm SQL Server, Redis, the API, the migration service, the
sync worker and the Angular app all appear healthy in the dashboard.
RESTRICTION: Fix only wiring that blocks startup. No features.
UTILIZATION: aspire-monitoring skill.
BEHAVIOR: Give me a one-line health summary per resource and tell me what you fixed.
```

## Phase 1 — Identity

### 1.1 DbContext and ApplicationUser - done
```
SCOPE: Add ApplicationUser : IdentityUser (DisplayName, CreatedAt, LastTenantId) in
AegisScribe.Domain/Managers/Models/Identity/ and AegisScribeDbContext : IdentityDbContext<ApplicationUser,
IdentityRole, string> in AegisScribe.Domain/Data/, registered in the API via the Aspire SQL Server EF
Core integration keyed to "aegisscribedb".
CONSTRAINT: .claude/rules/auth.md, backend.md; add-endpoint skill → "Target layout".
RESTRICTION: One context for everything. Do NOT create a second. The context lives in Domain, NOT in
the API — the repositories, the migration service and the sync worker all need it without referencing
a web host. Do NOT add domain entities. Do NOT create the migration yet.
BEHAVIOR: Show me the context and user shape, wait for approval, then confirm it resolves from DI.
```

### 1.2 Identity as the user store - done
```
SCOPE: Add Identity's user store plus the registration and password-management endpoints under
/api/v1/auth, through the full layer stack: AuthController → IAuthFacade → IAuthBusiness →
IUserDataLayer → IUserRepository (which wraps UserManager). Identity answers WHO SOMEONE IS; it does
not issue the app's tokens.
CONSTRAINT: .claude/rules/auth.md; add-endpoint skill.
RESTRICTION: The controller injects ONLY IAuthFacade — no UserManager, no validator. Validation runs
in the facade; the "never reveal whether an email has an account" rule lives in Business; UserManager
sits behind the repository. "It's only auth" is not an exemption from the stack. Do NOT add cookie
authentication, a login form, antiforgery or a session to the API — it is a pure token resource server
and 1B.5 would only delete them. Do NOT use MapIdentityApi's bearer mode: those tokens are opaque
Data-Protection blobs, not JWTs, and nothing outside the issuing process can validate them. Sign-in
arrives with OpenIddict's connect/authorize in 1B.4. No external provider, no Battle.net OAuth.
BEHAVIOR: Plan the layer stack and the logout handler, wait for approval, implement with per-layer
tests.
```

### 1.3 PlatformAdmin role - done
```
SCOPE: Add role support and seed exactly one Identity role at startup, idempotently: PlatformAdmin.
Add the PlatformAdmin authorization policy.
CONSTRAINT: .claude/rules/auth.md.
RESTRICTION: Do NOT add "Member", "Officer" or "Owner" as Identity roles. Those are tenant membership
and come in Phase 2 — adding them here grants that rank in every community a person belongs to, which
is the exact bug the tenancy model exists to prevent.
BEHAVIOR: Implement, and state plainly why only one role exists here.
```

### 1.4 Initial migration - done
```
SCOPE: Create the initial EF migration covering the Identity schema and apply it via the migration
service. Migrations live in AegisScribe.Domain/Migrations/: `dotnet ef migrations add <Name> --project
src/AegisScribe.Domain --startup-project src/AegisScribe.ApiService`.
CONSTRAINT: .claude/rules/backend.md.
RESTRICTION: Review before applying. Do NOT hand-edit the generated file.
BEHAVIOR: Show me the migration, wait for approval, apply, and confirm
`dotnet ef migrations has-pending-model-changes` is clean.
```

### 1.5 Identity smoke tests - done
```
SCOPE: Integration tests: register a user, confirm the user store round-trips, confirm password
management works, and confirm an authenticated endpoint returns a bare 401 when called anonymously.
CONSTRAINT: .claude/rules/auth.md.
RESTRICTION: Assert a bare 401 — .NET 10 no longer redirects API requests to a login page, and a test
expecting a 302 is testing a bug that was fixed. Do NOT write a cookie-login test; there is no cookie
auth on the API. End-to-end sign-in is tested in Phase 1B once OpenIddict exists.
BEHAVIOR: Run `dotnet test` green and report.
```

## Phase 1B — The edge: gateway, tokens and the web host

> Build this **before** tenancy, because every later endpoint is authenticated through it. Read
> `.claude/rules/gateway.md` and the token section of `.claude/rules/auth.md` first.

### 1B.1 The three public hosts - done
```
SCOPE: Add AegisScribe.Gateway (ASP.NET Core + Yarp.ReverseProxy 2.3) and AegisScribe.Web (thin static
host). Register both in the AppHost. Gateway references the API and Redis; Web references the gateway.
Three public hostnames: app.* (web), bff.* (gateway), api.* (the API).
CONSTRAINT: .claude/rules/gateway.md, aspire.md.
RESTRICTION: WithExternalHttpEndpoints() goes on the web host, the gateway AND the API — but NEVER the
sync worker or migration service. The API is public deliberately, for ops access; the browser still
only ever talks to bff.*. Do NOT use Aspire.Hosting.Yarp: it is a config-only proxy resource with
nowhere to put auth logic.
UTILIZATION: add-aspire-resource skill.
BEHAVIOR: Plan the two projects and the AppHost wiring, wait for approval, implement, and show me the
dashboard with all three of web, gateway and api carrying an external endpoint — and neither the sync
worker nor the migration service carrying one.
```

### 1B.2 YARP routes via service discovery - done
```
SCOPE: Configure the gateway to proxy /api/{**catch-all} to the API using
AddServiceDiscoveryDestinationResolver(), with the cluster address as the logical name http://api.
CONSTRAINT: .claude/rules/gateway.md.
RESTRICTION: No literal addresses or ports anywhere in the YARP config. Do NOT add auth yet.
BEHAVIOR: Implement, then show me an anonymous request reaching the API through the gateway.
```

### 1B.3 The web host serves the SPA - done
```
SCOPE: AegisScribe.Web serves the built Angular bundle with UseStaticFiles + MapFallbackToFile, and
exposes a runtime config endpoint returning the gateway's base URL.
CONSTRAINT: .claude/rules/gateway.md → "The web host".
RESTRICTION: The gateway URL must NOT be baked in at build time — the same artifact has to promote
between environments. No API calls, no session, no auth in this host. It is a file server with one
config endpoint.
BEHAVIOR: Implement, and show me the SPA loading from app.* and reading the gateway URL at runtime.
```

### 1B.4 OpenIddict, and the three registered clients ⚑ - done
```
SCOPE: Add OpenIddict to the API: core + EF Core stores against AegisScribeDbContext (UseOpenIddict()
on the context), the server with authorization/token/revocation/logout endpoints, authorization code +
PKCE and refresh token flows, and a seeding routine in the migration service registering exactly three
clients — aegisscribe-bff (confidential, web), aegisscribe-mobile (PUBLIC, ApplicationTypes.Native,
redirect aegisscribe://auth/callback, no secret), aegisscribe-ops (confidential, client credentials).
The connect/* controller keeps the protocol shape (claims identity, SignIn/Forbid, the sign-in form);
the user lookups behind it — password check with lockout, can-sign-in, roles — go through IAuthFacade.
CONSTRAINT: .claude/rules/auth.md → "OpenIddict issues the tokens"; add-endpoint skill → "Identity
counts as data".
RESTRICTION: The controller does NOT inject UserManager or SignInManager — they sit behind the user
repository in AegisScribe.Domain. Do NOT hand-write a token issuer — refresh rotation with replay detection, PKCE
verification and single-use codes are what this dependency is for. MapIdentityApi's bearer tokens are
NOT JWTs; don't try to use them. The mobile client gets NO secret in any form. RequireProofKeyForCodeExchange
is mandatory. Development signing/encryption certificates are selected BY ENVIRONMENT, never committed.
Verify the OpenIddict version and builder API against documentation.openiddict.com — this package moves
fast and 7.7.0 shipped in September 2026.
UTILIZATION: Plan mode.
BEHAVIOR: Plan the registration, the three client descriptors and the key strategy, wait for approval,
implement, and show me the discovery document at /.well-known/openid-configuration.
```

### 1B.4b Hardening the API's public edge - done
```
SCOPE: On the API: rate limiting partitioned per sub (authenticated), per client_id (fleet budget) and
per IP (anonymous only), returning 429 with Retry-After; OpenAPI gated to non-production; the generated
document committed at contract/openapi.v1.json.
CONSTRAINT: .claude/rules/backend.md → "The API's public edge".
RESTRICTION: The API has NO CORS policy. This survives mobile arriving — a native app has no origin and
sends no preflight, so CORS is irrelevant to it, while its absence still stops any browser reading an
API response cross-origin with a stolen token. Do NOT add one "to test something". Do NOT put a
per-IP-only limiter on authenticated routes: a carrier NAT puts a whole city behind one address.
BEHAVIOR: Implement, and test: a browser cross-origin fetch to the API is blocked; a 429 carries
Retry-After; OpenAPI 404s when the environment is Production.
```

### 1B.4c API versioning ⚑ - Done
```
SCOPE: Add Asp.Versioning.Http with URL-segment versioning. Every route becomes /api/v1/...; tenant
routes will be /api/v1/t/{tenantSlug}/... in Phase 2. Add the api-supported-versions and
api-deprecated-versions headers, plus Deprecation/Sunset support for later.
CONSTRAINT: .claude/rules/api-contract.md.
RESTRICTION: There is no unversioned route, including health checks people will script against. Do NOT
use header or query-string versioning — a URL segment survives a bug report, a log line and a curl.
The version segment comes BEFORE the tenant segment.
BEHAVIOR: Explain in one line why this is worth doing before the first real endpoint exists, then
implement and show me a versioned route responding.
```

### 1B.4d Styling the sign-in page - done
```
SCOPE: Restyle the connect/* controller's sign-in form (1B.4) — the page OpenIddict's authorization
endpoint renders when the caller isn't authenticated — to match the AegisScribe design system: colour,
type and spacing tokens, the wordmark, and the error and lockout states.
CONSTRAINT: aegisscribe-design-system skill; design/aegisscribe-armory.html §01 (colour), §03
(typography), §04 (space & form) and §05 (primitives — buttons, fields). There is no S-numbered mockup
for this screen; build it from those sections directly, and keep it deliberately plain — this is a
security-sensitive, low-frequency page, not a place for cleverness.
RESTRICTION: This page is server-rendered by AegisScribe.ApiService (plain Razor/HTML + CSS) — it
cannot import Angular, the scribe-* components, or src/web's SCSS pipeline. Transcribe the SAME token
VALUES into a small static stylesheet under the API's own wwwroot, exactly the way 5.2 transcribes
them for Angular: a copy, not a reinterpretation, and a literal hex here is the same defect it would be
in the SPA. No client-side framework, no build step — this is the one screen in the app that has to
render correctly with JavaScript off, since it sits in the middle of an OAuth redirect chain. The
interactive cookie this page uses to hold form state (1B.4) is scoped to the connect/* flow only — do
not confuse it with, or let it widen into, the API's bearer-only resource-server validation (1B.5).
BEHAVIOR: Plan the stylesheet's scope and where it lives, wait for approval, implement, and show me the
sign-in form, an invalid-credentials error, and a locked-out account — reached through the actual
redirect from bff.*'s /auth/login, not a standalone preview.
```

### 1B.5 The API becomes a token resource server - done
```
SCOPE: Add OpenIddict validation to the API with UseLocalServer() and UseAspNetCore(). Remove any
cookie authentication from the API.
CONSTRAINT: .claude/rules/auth.md.
RESTRICTION: Do NOT use AddJwtBearer. OpenIddict's access tokens are ENCRYPTED JWTs (JWE) and
AddJwtBearer cannot read them — the failure is a bare 401 with nothing useful logged. Do NOT reach for
DisableAccessTokenEncryption() to make the familiar API work. The auth scheme constant is
OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme. The API has no cookie auth, no login form,
no session and no antiforgery. Tokens carry sub and PlatformAdmin only; NOTHING tenant-shaped.
BEHAVIOR: Implement, then test a valid token, an expired one, a wrong-audience one and a tampered one.
```

### 1B.6 The gateway as a confidential OAuth client ⚑ - done
```
SCOPE: In the gateway: cookie authentication backed by a Redis ITicketStore; /auth/login running the
authorization code + PKCE flow against the API as client aegisscribe-bff; both tokens stored in the
session; a logout that revokes the refresh token at connect/revoke and then drops the session; a YARP
request transform injecting Authorization: Bearer from the session; single-flight server-side refresh.
CONSTRAINT: .claude/rules/gateway.md → "The gateway is an OAuth confidential client" and "Cookies
across subdomains".
RESTRICTION: Cookie is HttpOnly, Secure, SameSite=Lax, Domain=.aegisscribe.com, name prefixed
__Secure- (NOT __Host-, which forbids Domain). The client secret is an Aspire parameter, never a
literal. Login and logout are handled BY the gateway, not proxied. The browser must never receive a
token in any form. Refresh must be single-flight — a burst of proxied requests at expiry must produce
ONE refresh.
BEHAVIOR: Plan the session shape and the flow, wait for approval, implement, and assert the Set-Cookie
header carries every attribute above.
```

### 1B.7 Header sanitisation ⚑ - done
```
SCOPE: In the YARP transform, strip client-supplied Authorization and X-Forwarded-* headers before
setting our own. Configure ForwardedHeaders on the API to trust only the gateway's network.
CONSTRAINT: .claude/rules/gateway.md → "Header sanitisation".
RESTRICTION: This is the single most important control in the auth surface. Without it a caller
presents their own bearer token, the gateway forwards it, and the API cannot distinguish it from one
the gateway obtained.
BEHAVIOR: Implement, then write THE strip test: a request carrying a forged Authorization header
reaches the API carrying the gateway's token, not the forged one. Show me it passing.
```

### 1B.8 CORS, and the SPA auth client - done
```
SCOPE: CORS on the gateway naming the SPA origin from config with AllowCredentials. In Angular: the
functional interceptor setting withCredentials and X-Requested-With, plus login/logout calls.
CONSTRAINT: .claude/rules/gateway.md → "CORS", .claude/rules/auth.md → "Angular side".
RESTRICTION: AllowAnyOrigin() with AllowCredentials() is invalid, and reflecting the caller's origin is
the same as no CORS. The origin comes from config, never a literal. The SPA holds NO token: no
localStorage, no Authorization header in TypeScript, no refresh timer. CORS goes on the GATEWAY only —
the API still has none.
BEHAVIOR: Implement, and test that a preflight from an unlisted origin is rejected.
```

### 1B.9 Edge verification - done
```
SCOPE: Prove the boundary end to end.
UTILIZATION: @code-reviewer.
BEHAVIOR: Run and show me all seven: (1) the strip test; (2) no gateway response contains "eyJ" in
body, header or cookie; (3) an unlisted CORS origin is rejected; (4) anonymous character lookup still
works with no Authorization forwarded; (5) logout makes the next request anonymous immediately AND
revokes the refresh token, so a replay of it fails; (6) a request spanning token expiry succeeds with
no browser round trip; (7) a token request for aegisscribe-mobile WITHOUT a PKCE verifier is rejected.
```

## Phase 2 — Tenancy core

> **Be fussy here.** Everything after this phase depends on these seams being right, and a missing
> filter is silent. Read `.claude/rules/tenancy.md` before 2.1 and keep it open.

### 2.1 Tenant and membership entities - done
```
SCOPE: Add Tenant (Id, Slug, Name, TimeZoneId) and TenantMembership (TenantId, UserId, Role, JoinedAt)
with TenantRole { Member = 0, Officer = 10, Owner = 20 }. Migration included.
CONSTRAINT: .claude/rules/tenancy.md, add-tenant-entity skill.
RESTRICTION: The enum values are ordered on purpose — Owner implies Officer implies Member. Do NOT
make it a flags enum or reorder it. Do NOT add the query filter yet; that's 2.4.
BEHAVIOR: Plan the entities and indexes, wait for approval, migrate.
```

### 2.2 ITenantScoped and ITenantContext - done
```
SCOPE: Add the ITenantScoped marker interface (Guid TenantId) in
AegisScribe.Domain/Managers/Models/Domain/ and a scoped ITenantContext exposing the resolved tenant in
AegisScribe.Domain/Context/. ITenantContext THROWS when read with no tenant resolved.
CONSTRAINT: .claude/rules/tenancy.md.
RESTRICTION: It must fail closed. Do NOT return Guid.Empty, a default, or a nullable that callers can
ignore — a silent default is how every row ends up in one tenant.
BEHAVIOR: Show me the failure behaviour explicitly, then implement.
```

### 2.3 Tenant resolution middleware - done
```
SCOPE: Middleware in the API that resolves {tenantSlug} from the route, loads the Tenant, verifies the
caller has a TenantMembership, and populates ITenantContext. Routes: /api/v1/t/{tenantSlug}/...
tenant-scoped; /api/v1/auth, /api/v1/platform, /api/v1/characters tenant-less. The lookups go through
ITenantResolutionFacade → ITenantBusiness → ITenantDataLayer → ITenantRepository in AegisScribe.Domain.
CONSTRAINT: .claude/rules/tenancy.md, auth.md; add-endpoint skill (the layer stack).
RESTRICTION: The tenant comes ONLY from the route. Do NOT read it from a header, query string, body,
or ApplicationUser.LastTenantId. No membership must produce 404, NOT 403 — a 403 confirms the tenant
exists. That "unknown and non-member look identical" rule is a domain rule and lives in Business. The
middleware does NOT inject the DbContext or UserManager.
BEHAVIOR: Plan the pipeline position and the 404 behaviour, wait for approval, implement, and add a
test for each of: valid member, non-member, unknown slug, tenant-less route.
```

### 2.4 Global query filter, by convention - done
```
SCOPE: In OnModelCreating, loop over every ITenantScoped entity type and apply
HasQueryFilter(e => e.TenantId == _tenantContext.TenantId).
CONSTRAINT: .claude/rules/tenancy.md.
RESTRICTION: Apply it BY CONVENTION, not per entity. A developer adding entity number forty must not
have to remember this step — that is the entire point. Tenant resolution (2.3) reads TenantMembership
BEFORE any tenant is resolved, so if TenantMembership is ITenantScoped, that one lookup needs a
deliberate answer — plan it; do NOT reach for IgnoreQueryFilters() on the request path.
BEHAVIOR: Show me the convention loop, wait for approval, implement.
```

### 2.5 TenantId interceptor - done
```
SCOPE: A SaveChanges interceptor in AegisScribe.Domain/Data/ that stamps TenantId from ITenantContext
on every added ITenantScoped entity, and throws if one arrives carrying a different tenant.
CONSTRAINT: .claude/rules/tenancy.md.
RESTRICTION: Business code must never assign TenantId. Add a test that a cross-tenant stamp throws.
BEHAVIOR: Implement and show me both paths under test.
```

### 2.6 Tenant policies - done
```
SCOPE: TenantRoleRequirement + handler (in the API's Auth/) reading ITenantContext and the caller's
membership, satisfying when membership.Role >= required. Policies: TenantMember, TenantOfficer,
TenantOwner. The membership lookup goes through the tenant facade from 2.3, not the DbContext.
CONSTRAINT: .claude/rules/auth.md; add-endpoint skill (the layer stack).
RESTRICTION: Fail closed — no resolved tenant or no membership is a failure, never a default to
Member. Encode the implication once in the handler; no endpoint lists three roles.
BEHAVIOR: Plan the handler, wait for approval, implement, test each rank against each policy.
```

### 2.7 Me, tenants and membership endpoints  done
```
SCOPE: GET /api/v1/me (the user and their memberships with roles), POST /api/v1/tenants (create a community,
creator becomes Owner), GET/PATCH /api/v1/t/{slug} (read and rename, Owner only).
CONSTRAINT: add-endpoint and add-tenant-entity skills.
RESTRICTION: A tenant must always have at least one Owner — the demote and remove paths refuse the
last one. That is a domain rule and lives in Business. /me already runs through IMeFacade — extend
that stack with memberships; do NOT add a second path. Once GET /api/v1/t/{slug} exists, delete the
diagnostic TenantPingController and point the 2.3 tenant-resolution tests at the real route.
BEHAVIOR: Plan the layer stack, wait for approval, implement with per-layer tests.
```

### 2.7b Community identity — slug and time zone - done
```
SCOPE: Make a community nameable by a person rather than by a curl. Extend 2.7's stack (ITenantsFacade
→ ITenantBusiness → ITenantDataLayer → ITenantRepository) with: slug derivation from the display name
in Business (lowercase, ASCII-folded, non-alphanumerics to hyphens, collapsed, trimmed, 3–48 chars);
a reserved-slug list; GET /api/v1/tenants/slug-available?slug=... so a form can ask before it submits;
and TimeZoneId validated at the edge.
CONSTRAINT: add-endpoint skill — the availability check is an endpoint like any other and owes the full
layer stack; .claude/rules/tenancy.md.
RESTRICTION: 2.7 is done — this is ADDITIVE. Do not rewrite the create path; extend it. The slug is the
community's URL forever, so derivation and the reserved list are Business rules with tests, NOT a
TypeScript helper — the mobile app creates communities too (13B) and must not reimplement them. Reserve
at least: new, create, edit, admin, api, auth, platform, t, tenants, me, assets, static, and every
single-character slug. Availability is a HINT, not a reservation: the unique index stays the authority
and a lost race surfaces as 409 from create, never as a silently renamed second tenant. The endpoint
requires an authenticated caller and is rate-limited — it answers "is this free" and nothing else,
never who holds a taken slug, or it becomes a community-enumeration oracle on a public host. TimeZoneId
is validated against TimeZoneInfo and refused with a 400 at the edge; an unparseable zone must not
survive to the first calendar render (9.1).
BEHAVIOR: Plan the derivation rules and the reserved list, wait for approval, implement with per-layer
tests covering the fold cases (accents, punctuation, doubled and trailing hyphens, an all-punctuation
name, a name that folds to nothing), the reserved rejection, the 409 race, and a rejected zone id.
```

### 2.8 Tenant cache keys - done
```
SCOPE: Establish the cache key convention in the facade base (AegisScribe.Domain/Facade/):
tenant-scoped ServiceModels key as t:{tenantId}:..., global ServiceModels key bare. Add a helper that
makes the wrong one awkward.
CONSTRAINT: .claude/rules/tenancy.md; add-endpoint skill → "Facade".
RESTRICTION: Both directions are bugs — a bare tenant key leaks across communities, a tenant-prefixed
global key fragments the shared cache N ways.
BEHAVIOR: Show me the helper's signature before writing it.
```

### 2.9 The two-tenant test harness ⚑ - done
```
SCOPE: Build the reusable test fixture every later feature will use: seeds two tenants with similar
data and two users, one in each, and exposes authenticated clients for both. Prove it works against
the endpoints from 2.7.
CONSTRAINT: .claude/rules/tenancy.md → "Testing".
RESTRICTION: Assertions go through the ENDPOINT, not the repository — a repository-level test misses
middleware, policy and cache bugs, which is most of them.
UTILIZATION: When done, run @tenant-isolation-auditor over everything built so far.
BEHAVIOR: This harness is the thing that makes every later isolation test cheap, so design it before
writing it. Show me the fixture's shape, wait for approval, implement, and report the auditor's
findings section by section.
```

## Phase 3 — Global reference domain

### 3.1 Realm and Character - done
```
SCOPE: Add Realm and Character (realm, name, level, class, spec, item level, faction, LastSyncedAt,
Blizzard source id) plus CharacterEquipment / EquippedItem. Migration included.
CONSTRAINT: .claude/rules/tenancy.md → the two zones; backend.md.
RESTRICTION: These are GLOBAL. No TenantId, no query filter, ever. Character natural key is
(realmSlug, name) lowercased — Blizzard character ids don't survive renames and transfers.
BEHAVIOR: State plainly why these are global before writing, then implement.
```

### 3.2 Items, professions, recipes - done
```
SCOPE: Add Item (Blizzard id, quality, slot, item level, SearchText), Profession, Recipe, ReagentSlot.
Migration included.
CONSTRAINT: Same as 3.1 — global zone.
RESTRICTION: Model only fields the app will render. Leave the Embedding column out; it arrives in 12.4
with its own migration. Compose SearchText deliberately — it's what gets embedded later.
BEHAVIOR: Show me the SearchText composition and the entity shapes, wait for approval.
```

### 3.3 Guild and GuildMember - done
```
SCOPE: Add Guild and GuildMember, the latter carrying BlizzardRank (0–9, from the game).
CONSTRAINT: .claude/rules/tenancy.md.
RESTRICTION: Global zone. BlizzardRank is what the GAME says and is NOT the community's own rank —
that's TenantRank in Phase 7 and neither derives from the other. Do not conflate them.
BEHAVIOR: Implement and state the distinction in a comment on the entity.
```

### 3.4 Seed data - done
```
SCOPE: Seed two tenants ("Ashes of Dawn", "Emberwatch"), users in each, two realms, ~12 characters
with full gear, a guild with a roster, and enough items and recipes to demo crafting.
CONSTRAINT: .claude/rules/tenancy.md.
RESTRICTION: Two tenants, not one — every isolation test from here depends on it. Data must be clearly
fictional but use real identifier formats.
BEHAVIOR: Implement, then confirm both tenants' data is queryable and distinct.
```

### 3.5 Apply and verify - done
```
SCOPE: Apply all pending migrations, confirm the schema, and confirm has-pending-model-changes is clean.
RESTRICTION: Review each migration before applying.
BEHAVIOR: Report the table list, showing which carry TenantId and which don't, and confirm it matches
the two-zone table in .claude/rules/tenancy.md.
```

## Phase 4 — The first vertical (global, no Blizzard)

### 4.1 Character repository - done
```
SCOPE: ICharacterRepository / CharacterRepository — find by realm+name with Includes, a paged list
projecting to a summary ServiceModel in SQL, and ExecuteInTransactionAsync.
CONSTRAINT: add-endpoint skill, "Repository".
RESTRICTION: EF only. No rules, cache or validation. ExecuteInTransactionAsync takes a CALLBACK — the
Aspire retry execution strategy refuses a caller-opened transaction.
BEHAVIOR: Plan the method signatures, wait for approval, implement with integration tests against real
SQL Server.
```

### 4.2 Character data layer - done
```
SCOPE: ICharacterDataLayer / CharacterDataLayer — for now a pass-through to the repository.
CONSTRAINT: add-endpoint skill, "DataLayer".
RESTRICTION: A one-line pass-through is CORRECT and expected — the seam is the point, and it's what
lets Phase 6 add cache-first Blizzard reads without touching Business. Do not "simplify" it away.
BEHAVIOR: Implement with unit tests proving delegation.
```

### 4.3 Character business - done
```
SCOPE: ICharacterBusiness / CharacterBusiness — entity → ServiceModel mapping on detail reads, list
pass-through.
CONSTRAINT: add-endpoint skill, "Business".
RESTRICTION: Depends on ICharacterDataLayer only. No validator, no cache, no DbContext.
BEHAVIOR: Implement with unit tests over a mocked data layer.
```

### 4.4 Character facade - done
```
SCOPE: ICharacterFacade / CharacterFacade — validation via FluentValidation, read-through Redis cache
of ServiceModels.
CONSTRAINT: add-endpoint skill, "Facade". Cache keys per 2.8.
RESTRICTION: Character data is GLOBAL — cache under a bare key, NOT tenant-prefixed. Tests must cover
cache hit, cache miss and validation failure; that trio is the most-skipped set in the repo.
BEHAVIOR: Implement, run the trio green, report.
```

### 4.5 Character controller - done
```
SCOPE: GET /api/v1/characters/{realm}/{name} and GET /api/v1/characters (paged, filterable by realm).
CONSTRAINT: add-endpoint skill.
RESTRICTION: TENANT-LESS — global reference data, anonymous, no tenant policy, no tenantSlug in the
route. This is the app's front door. Do NOT put it under /api/v1/t/.
BEHAVIOR: Implement and show me a curl returning a ServiceModel.
```

### 4.6 Vertical review - done
```
SCOPE: Review the whole character vertical before anything copies it.
UTILIZATION: Run @skills-evals, then @code-reviewer, then @tenant-isolation-auditor.
RESTRICTION: Fix findings in place; do not redesign.
BEHAVIOR: Give me all three reports, then fix what they flag and re-run. This vertical is the template
every later feature copies, so it is worth the extra pass.
```

## Phase 5 — Frontend foundation

### 5.1 Angular strictness and structure - done
```
SCOPE: Enable strict TypeScript, set up src/web/src/app/models/, core/, shared/, features/, and
provideHttpClient with functional interceptors.
CONSTRAINT: .claude/rules/frontend.md.
RESTRICTION: No `any`. No class-based interceptors or guards.
BEHAVIOR: Implement and confirm `ng build` clean.
```

### 5.2 Design tokens - done
```
SCOPE: Transcribe the token set into src/web/src/styles/_tokens.scss and load the four fonts.
CONSTRAINT: aegisscribe-design-system skill; references/tokens.md is the transcription target.
RESTRICTION: A COPY, not a reinterpretation — same names, same values, same comments where they explain
a decision. After this, a literal hex anywhere under src/web/src/app/ is a defect.
BEHAVIOR: Show me the transcription diff against the reference before committing.
```

### 5.3 Shared primitives, part 1 - done
```
SCOPE: scribe-item-cell, scribe-stat-tile, scribe-status-pill.
CONSTRAINT: aegisscribe-design-system skill; §06 and §05 of the design reference.
RESTRICTION: The item cell is an <a> with target/rel, built from blizzardItemId. Quality sets the left
border and the name colour, nothing else; rare and epic names use the -text variants. A flagged cell
tints its background and adds a word — it does NOT repaint the quality border.
BEHAVIOR: Implement with render tests, then run @design-review.
```

### 5.4 Shared primitives, part 2 - done
```
SCOPE: scribe-filter-chip, scribe-meter, scribe-empty-state, scribe-skeleton, scribe-ai-badge,
scribe-local-time.
CONSTRAINT: aegisscribe-design-system skill.
RESTRICTION: scribe-local-time renders the tenant zone by default with the viewer's on hover, and
ALWAYS labels which is which. Skeletons are shape-matched, never spinners.
BEHAVIOR: Implement with render tests, run @design-review.
```

### 5.5 Auth interceptor and guards - done
```
SCOPE: Functional interceptor setting withCredentials centrally and handling 401 once; functional auth
guard and tenant guard.
CONSTRAINT: .claude/rules/auth.md → "Angular side".
RESTRICTION: withCredentials set centrally, never per call site. A 404 on a tenant route means "not
yours" — route to the tenant picker, never render an error, and never distinguish "doesn't exist" from
"not a member".
BEHAVIOR: Plan the interceptor's 401 and 404 handling, wait for approval, implement.
```

### 5.6 Tenant chrome - done
```
SCOPE: The app header with scribe-tenant-switcher, and the tenant-picker route. Routes take the shape
/t/:tenantSlug/...
CONSTRAINT: aegisscribe-design-system skill; §07 of the design reference; .claude/rules/frontend.md.
RESTRICTION: Services build API URLs from the ACTIVE ROUTE's tenant, never from a stored variable —
the two drift and the user sees one community's data in another's chrome. Switching navigates; it
never mutates hidden state.
BEHAVIOR: Plan the routing and the URL construction, wait for approval, implement, run @design-review.
```

### 5.6b Landing page ⚑ - done
```
SCOPE: The app's FIRST route, at '/' — a signed-out hero with "Register" and "Log in" calls to action,
plus the registration form itself (display name, email, password → POST /api/v1/auth/register through
the gateway, per 1.2). Root routing: '/' resolves to this page when signed out, to the tenant-picker
(5.6) when signed in.
CONSTRAINT: aegisscribe-design-system skill; .claude/rules/auth.md → "Angular side"; the 5.3/5.4
primitives.
RESTRICTION: It renders before any API call can succeed or fail, so it must not depend on a resolved
tenant or an authenticated /me call. "Log in" is a full-page navigation to bff.*'s /auth/login (1B.6),
NOT an HttpClient call — the OAuth code+PKCE flow is a browser redirect and an XHR cannot follow it
anywhere useful. Registration IS a normal JSON POST — Identity account creation (1.2) is not part of
the token flow — and on success navigates the SAME way to /auth/login; creating an account does not
sign you in, the interactive sign-in step (styled in 1B.4d) still has to happen. Never fabricate a
session or store a token from the register response — the SPA holds no tokens, full stop.
BEHAVIOR: Plan the routing (where Angular hands off to a full navigation) and the register form's
validation, wait for approval, implement, run `ng test`, @design-review.
```

### 5.6c Create a community - done
```
SCOPE: The screen that turns a signed-in account into an Owner. A create-community route, reached from
the tenant-picker's empty state and its header (5.6): community name, the slug the server derived shown
read-only with an "Edit" affordance, and a time zone defaulted from the browser. POST /api/v1/tenants
through the gateway; on success navigate to /t/:slug.
CONSTRAINT: aegisscribe-design-system skill; .claude/rules/frontend.md; the 5.3/5.4 primitives; 2.7's
create endpoint and 2.7b's availability check.
RESTRICTION: 5.6 and 5.6b are done — this is ADDITIVE; the only edit to existing code is the picker's
empty state. That state currently reads "Ask an officer for an invite, or create one of your own" with
no way to do it (tenant-picker.html) — that dead end is the bug this prompt closes. This screen is NOT
drawn in design/aegisscribe-armory.html: compose it from §05's primitives and §07's chrome and invent
nothing — a literal hex under src/web/src/app/ is a defect here as everywhere. The slug rendered is the
SERVER's answer, debounced, never a TypeScript port of 2.7b's derivation. A 409 on submit re-renders
the form with the taken slug flagged and every other field intact. Creating does not set a stored
"current tenant" — there is no such variable (5.6) — it navigates. All four states, including the
submit-in-flight one.
BEHAVIOR: Plan the route, the debounce, and the 409 path, wait for approval, implement, run `ng test`,
then @design-review and @api-contract-checker.
```

### 5.7 Character profile screen - done
```
SCOPE: character-profile with character-banner, two equipment-rails, the weapons row, tabs, and the
progression / profession / provenance panels.
CONSTRAINT: aegisscribe-design-system skill; screen S1.
RESTRICTION: All four states — loading, empty, DEGRADED, error. Build the degraded state now behind a
ServiceModel flag even though nothing can trigger it until Phase 6; a state retrofitted later is a
state nobody designed. Class colour is an inline --class-color; no class-to-hex map in TypeScript.
BEHAVIOR: Implement, run `ng test`, then @design-review and @api-contract-checker.
```

## Phase 6 — Blizzard

### 6.1 Gateway and OAuth - done
```
SCOPE: AegisScribe.Domain/Integration/Blizzard/ — IBlizzardGateway, typed HttpClient, client-credentials OAuth against the
REGIONAL token endpoint, token cached and refreshed once under a lock. Credentials as Aspire parameters.
CONSTRAINT: .claude/rules/external.md; add-external-sync skill; references/blizzard-terms-and-limits.md.
RESTRICTION: Bearer header only — ?access_token= is disallowed and the secret-guard hook blocks it.
Use the regional oauth host, not oauth.battle.net. Missing credentials degrade, they do NOT throw.
BEHAVIOR: Plan the token lifecycle, wait for approval, implement, and show me what happens with no
credentials configured.
```

### 6.2 Rate limiter - done
```
SCOPE: A shared rate limiter in front of the gateway, sized under the documented caps, with 429 backoff
using the response's retry hint.
CONSTRAINT: references/blizzard-terms-and-limits.md.
RESTRICTION: Every gateway method takes a lease. 36,000/hour is contractual, not advisory.
BEHAVIOR: Implement with tests for lease acquisition and 429 backoff.
```

### 6.3 Character fetch - done
```
SCOPE: FetchCharacterAsync and FetchEquipmentAsync, returning DOMAIN ENTITIES.
CONSTRAINT: add-external-sync skill; references/blizzard-endpoints.md — read it, don't guess the path.
RESTRICTION: Every call carries namespace and locale. A 404 returns null, not an exception — a
character that doesn't exist is a normal answer. Blizzard response types never leave this folder.
BEHAVIOR: Implement with tests using CAPTURED REAL response bodies as fixtures, asserting the
namespace, the bearer header, the 404-returns-null path, and the 429 backoff.
```

### 6.4 Cache-first reads ⚑ - done
```
SCOPE: Wire IBlizzardGateway into CharacterDataLayer: local first, fetch only when missing or stale,
persist what comes back, fall back to the stale row when Blizzard is unreachable. Staleness policy as
injected config.
CONSTRAINT: add-endpoint skill step 6; .claude/rules/external.md.
RESTRICTION: No TTL may exceed 30 days — a Terms of Use obligation with its own test. The gateway call
happens OUTSIDE any transaction callback; the callback is retryable and an HTTP call inside one fires
twice.
BEHAVIOR: Implement the full matrix under test — fresh, stale, gateway-failure-falls-back, missing —
plus the ≤30-day config assertion. Then show me a never-synced character arriving from Blizzard.
```

### 6.4b Realm catalogue ⚑ - done
```
SCOPE: A global sync that fills Realm for every realm in each configured region, connected-realm
grouping included, so realms stop being discovered one at a time by whoever happens to look up a
character on one. It is also what a realm picker reads.
CONSTRAINT: add-external-sync skill; references/blizzard-endpoints.md. Go via
/data/wow/connected-realm/index and then each connected realm — that document carries its own id AND
every realm in the group, so one pass fills BlizzardConnectedRealmId in ~1+N calls per region.
/data/wow/realm/index looks cheaper and is not: it omits connected_realm, costing a call per realm.
RESTRICTION: dynamic- namespace, never static-. Runs GLOBALLY, once, outside any tenant — realm data
is identical for every community, so per-tenant would multiply calls by tenant count. Bounded
concurrency over the connected-realm list; never Task.WhenAll over it. Idempotent and resumable from
the store: an interrupted pass re-runs without duplicating a realm. Realm carries LastSyncedAt and the
same 30-day obligation as everything else Blizzard-derived. The lazy FetchRealmAsync from 6.4 stays —
it is the fallback for a realm that appears between passes, not dead code.
BEHAVIOR: Plan the concurrency bound and the upsert key, wait for approval, implement, test that a
second run produces no duplicate rows — same count, same row ids, LastSyncedAt advanced. It must
advance: that column is the 30-day compliance clock, so "writes nothing" would be the wrong assertion.
Then show me the realm count per region.
```

### 6.5 Sync worker: stale refresh - done
```
SCOPE: A worker job that finds rows approaching the refresh deadline and re-fetches them, with bounded
concurrency and a per-run budget, idempotent and resumable from the store.
CONSTRAINT: add-external-sync skill step 6.
RESTRICTION: Never Task.WhenAll over an unbounded collection reaching the gateway. This job runs
GLOBALLY, outside any tenant — do NOT iterate tenants; the data is global and per-tenant syncing
multiplies API calls by tenant count.
BEHAVIOR: Plan the selection query and concurrency bound, wait for approval, implement, test idempotency.
```

### 6.6 Per-tenant sync budget - done
```
SCOPE: ITenantSyncBudget, drawn on by any tenant-triggered sync, returning 429 with a retry hint when
exhausted. Expose remaining budget to tenant Owners.
CONSTRAINT: .claude/rules/external.md → "Sync is global; tenant-triggered work has a budget";
add-endpoint skill for the Owner-facing budget route.
RESTRICTION: One community must not be able to starve another. Budget exhaustion is a 429, not a silent
queue.
BEHAVIOR: Implement with a two-tenant test proving A cannot consume B's budget.
```

### 6.6b Guild roster ⚑ - done (also completes 8.1)
```
SCOPE: Guild and GuildMember filled from /data/wow/guild/{realmSlug}/{nameSlug}/roster — the one
Blizzard endpoint that genuinely batches. An officer links their community's guild and triggers a
sync; the worker keeps linked guilds inside the 30-day window afterwards.
CONSTRAINT: add-external-sync skill; references/blizzard-endpoints.md → "Batching" and the guild
namespace trap (the path is /data/wow/ but the namespace is profile-). Tenant-triggered, so it draws
on ITenantSyncBudget (6.6).
RESTRICTION: Guild and GuildMember are GLOBAL (tenancy.md) — no TenantId, no query filter. Which guild
a community claims is tenant-scoped and belongs on the tenant, not on the guild. The roster carries NO
item level and NO equipment: do NOT fan out to a character fetch per member to fill them in — that
turns a one-call sync into 800 and is exactly what the budget exists to stop. A member who has left
must disappear from GuildMember, or the roster only ever grows. BlizzardRank is what the GAME says;
it never derives from, and never writes to, TenantRank.
BEHAVIOR: Plan the diff — joins, leaves and rank changes — and the budget cost of one sync, wait for
approval, implement, test that a departed member is removed, that a second sync over identical data
changes nothing but LastSyncedAt, and that no character fetch is triggered.
```

### 6.7 Compliance pass ⚑
```
SCOPE: Audit what Phase 6 actually built — the Blizzard gateway, the realm catalogue, the cache-first
read, both refresh workers, the per-tenant budget and the guild roster. NOT WarcraftLogs (Phase 11)
and NOT Discord (Phase 10): auditing an integration that does not exist yet produces findings nobody
can act on, which is how a compliance pass becomes noise people learn to skip.
UTILIZATION: @external-compliance, then @tenant-isolation-auditor.
CONSTRAINT: .claude/rules/external.md and references/blizzard-terms-and-limits.md are the rule source;
CLAUDE.md → Restrictions is the tiebreaker. Read them rather than auditing from memory.
RESTRICTION: Triage every finding into exactly one of three buckets before fixing anything, because
"fix the blockers" is otherwise an instruction to build Phase 14 early:
  (1) LIVE VIOLATION — fix it in this pass. Two kinds, and the second is the one a narrow reading
      misses:
      (a) We are storing or fetching Blizzard data in a way that breaches a term RIGHT NOW: a refresh
          interval over 30 days, an unbounded fan-out, a missing namespace, a token in a query
          string, a per-tenant sync of global data, a server-side Wowhead request, missing
          attribution.
      (b) A RULE FILE ASSERTS A CONTROL EXISTS AND IT DOES NOT. These have no owning phase, so they
          are not bucket (2), and the rule is not wrong, so they are not bucket (3) — left untriaged
          they survive every pass forever while the rule file quietly lies. The first run of this
          prompt found exactly one: external.md said "there is a test that pins a handful of known
          ids" for the Wowhead id-space assumption, and there was not.
  (2) DEFERRED BY PLAN — a real obligation whose implementing phase has not run. Record it, do not
      build it. Known set as of this prompt: the deletion path and the SyncSuppression tombstone are
      14.1; WarcraftLogs is 11; Discord is 10. For each, assert the WEAKER claim that Phase 6 has not
      made it harder — every Blizzard-derived entity still carries a stable source id the erasure
      routine will be able to target, and no sync bypasses the seam it will hook into.
  (3) FALSE POSITIVE — the checklist and the code disagree because the code is better. The known one:
      "every gateway method takes a rate-limiter lease" is satisfied structurally by
      BlizzardRateLimitHandler sitting innermost on the typed client, so NO gateway method calls
      AcquireAsync and none should. Fix the CHECKLIST, not the code, and say so in the report.
A finding demoted to (2) or (3) must carry the reason in one line. Silent demotion is how a real
violation gets filed as "known".
BEHAVIOR: Run both agents, triage, fix only bucket (1), re-run to confirm those are gone, and leave
buckets (2) and (3) written down where the next phase will find them — deferred items as a note on
the prompt that owns them, checklist corrections in the agent file itself. Then tell me the count in
each bucket and what changed.
```

## Phase 7 — Roster and ranks

### 7.1 TenantRank - done
```
SCOPE: TenantRank (TenantId, Name, SortOrder, Colour) with officer-only CRUD.
CONSTRAINT: add-tenant-entity and add-endpoint skills.
RESTRICTION: Tenant-scoped — TenantId, query filter, tenant cache key, TenantOfficer policy. Colour is
tenant config surfaced as --rank-color, not a design token.
BEHAVIOR: Plan, approve, implement, two-tenant test.
```

### 7.2 RosterEntry - done
```
SCOPE: RosterEntry (TenantId, CharacterId → global Character, TenantRankId, OfficerNote, JoinedAt).
CONSTRAINT: add-tenant-entity skill.
RESTRICTION: Tenant-scoped, holding an FK into a GLOBAL entity — that direction only. A global entity
must never hold an FK to a tenant-scoped one. Indexes start with TenantId.
BEHAVIOR: Implement with the two-tenant test.
```

### 7.2b Claiming a character ⚑
```
SCOPE: CharacterClaim (TenantId, CharacterId → global Character, UserId, ClaimedAt), through the full
layer stack — this is the add-endpoint skill's own worked example (ClaimCharacterViewModel,
CharacterClaim, the one-claim-per-character rule, resource authorization in Business). Self-service
claim and unclaim; an officer may CLEAR (not reassign) another member's mistaken claim.
CONSTRAINT: add-tenant-entity and add-endpoint skills; .claude/rules/tenancy.md (CharacterClaim is
already listed there as tenant-scoped) and external.md (already names it in the erasure enumeration).
RESTRICTION: Tenant-scoped — TenantId, query filter, tenant cache key, TenantMember policy. At most one
UserId per (TenantId, CharacterId) — read the existing claim before inserting; a second claim on an
already-claimed character in this tenant is refused (409), not silently overwritten, and that check is
Business's, per the skill's own worked table. A member may only claim or unclaim FOR THEMSELVES, never
name another user in the request — an officer's clear is a separate, audited endpoint that frees the
claim, it does not hand it to someone else. Claiming is NOT Battle.net verification (out of scope, see
CLAUDE.md Scope) and asserts nothing to Blizzard — it grants no extra permission; RosterEntry rank and
OfficerNote stay TenantOfficer-gated exactly as before. A claim does not require the character to
already be on this tenant's roster (7.4's add is a separate act from claiming).
BEHAVIOR: Plan the endpoints and the conflict response, wait for approval, implement, test: self-claim,
double-claim 409, self-unclaim, officer clear with an audit row, and the two-tenant test — claiming in
tenant A must leave the same Character's standing in tenant B untouched.
```

### 7.3 Alt linking
```
SCOPE: MainRosterEntryId on RosterEntry, plus endpoints to link and unlink alts.
CONSTRAINT: add-tenant-entity and add-endpoint skills.
RESTRICTION: Alt linking lives on RosterEntry, NOT on Character — who is somebody's main is a
community's judgement, and the same player may be organised differently in two communities. A member
may link/unlink alts only among entries THEY claim (7.2b); an officer may link any. Reject cycles and
self-links in Business.
BEHAVIOR: Implement with tests for the cycle and self-link rejections.
```

### 7.4 Roster endpoints
```
SCOPE: GET /api/v1/t/{slug}/roster (paged, sortable, alt-grouped, claim state included), POST to add a
character, PATCH rank and note, DELETE to remove.
CONSTRAINT: add-endpoint and add-tenant-entity skills.
RESTRICTION: Reads TenantMember, writes TenantOfficer. Claim/unclaim/clear are 7.2b's endpoints, not
these — adding a character to the roster and claiming one are different acts with different actors.
Every officer write to someone else's entry writes an AuditLog row.
BEHAVIOR: Implement with per-layer tests plus 401, 403, and the three tenancy assertions.
```

### 7.5 Roster UI
```
SCOPE: roster-table with tenant rank pills, in-game rank alongside, alt grouping and sorting; plus
rank-manager for officers.
CONSTRAINT: aegisscribe-design-system skill; screen S3 and §08.
RESTRICTION: Show BOTH ranks — the community's and the game's — and never imply one derives from the
other. All four states.
BEHAVIOR: Implement, `ng test`, @design-review.
```

### 7.5b Claim UI
```
SCOPE: A claimed-by indicator and claim/unclaim action on roster-table (7.5) rows; the "Claimed by
you" pill and claim/unclaim button on character-profile (5.7, screen S1).
CONSTRAINT: aegisscribe-design-system skill; screen S1's "Claimed by you" pill and the "Claim
character" button in §05.
RESTRICTION: character-profile's route is the tenant-less front door (5.7) — it must keep working with
no active tenant, showing neither the pill nor the button. When reached with an active tenant (e.g.
navigated to from roster-table, or the SPA otherwise has one selected), the same component takes the
tenant as an optional input and shows claim state for it; don't fork a second route or duplicate the
component to get there. An unclaimed row shows "Claim" to everyone with TenantMember; a row claimed by
someone else shows who, with no action for a non-officer.
BEHAVIOR: Implement, `ng test`, @design-review.
```

### 7.6 Roster audit
```
SCOPE: Audit the roster vertical.
UTILIZATION: @tenant-isolation-auditor, @skills-evals.
BEHAVIOR: Report and fix.
```

## Phase 8 — Guild sync and membership - done

### 8.1 Guild roster sync - done (built early, as 6.6b)
```
Built out of order. 6.6b needed a tenant-triggered sync with a real budget cost, and the guild roster
is the one Blizzard endpoint that batches — so the gateway method, the profile- namespace assertion,
the bounded-concurrency worker and the join/leave/rank diff all landed there instead. Nothing remains
here; see 6.6b for what was built and IBlizzardGateway.FetchGuildRosterAsync for the seam.

Left as a marker rather than deleted: the ordering is the useful part of the record, and a reader
arriving from 8.2 should find out here that the sync already exists rather than building a second one.

SCOPE: Gateway methods and a worker job to sync a guild and its roster from Blizzard.
CONSTRAINT: add-external-sync skill; references/blizzard-endpoints.md.
RESTRICTION: Guild endpoints live under /data/wow/ but take the profile-{region} namespace — getting
this wrong returns a 404 that reads as "guild doesn't exist". Assert it in a test. A roster is the
classic unbounded fan-out; bound the concurrency.
BEHAVIOR: Implement, test the namespace assertion explicitly.
```

### 8.2 Importing a linked guild's roster - done
```
SCOPE: Import a linked guild's members into RosterEntry rows. Linking itself is already built (6.6b:
TenantGuild, and the Officer-gated link/unlink/re-sync endpoints) — this is only the import, which had
to wait because RosterEntry does not exist until 7.2.
CONSTRAINT: add-tenant-entity and add-endpoint skills.
RESTRICTION: Officer, not Owner — linking and importing are roster upkeep, the same class of act as
re-syncing, and the per-tenant budget rather than the policy is what bounds abuse. Import creates
RosterEntry rows and must NOT copy character data: the Character is global and already stored by the
guild sync, so a RosterEntry holds an FK to it and nothing else. Import does NOT create CharacterClaim
rows — Blizzard has no notion of which of your members plays which character; every imported row
starts unclaimed and each member claims their own via 7.2b. Two communities may link and import the
same guild, and neither import may touch the other's rows. Import is idempotent: running it twice
must not double the roster, and a member already on the roster keeps their existing rank and note.
BEHAVIOR: Plan the import — in particular what happens to a RosterEntry whose character has since left
the guild, which is a judgement call rather than an obvious delete — wait for approval, implement,
two-tenant test on the SAME guild.
```

### 8.3 Membership lifecycle - done
```
SCOPE: Invitations (single-use, expiring), join requests, role changes, removal — with the last-owner
rule.
CONSTRAINT: .claude/rules/auth.md → "Membership lifecycle"; add-endpoint and add-tenant-entity skills.
RESTRICTION: Officer may promote to Member/Officer; only Owner may create another Owner or demote an
Officer. A tenant must always have at least one Owner — demote and remove both refuse the last one,
as a Business rule. Every one of these writes an AuditLog row.
BEHAVIOR: Plan the state transitions, wait for approval, implement, test every refusal.
```

### 8.3b Accepting an invite ⚑ - done
```
SCOPE: The invitee's half of 8.3. POST /api/v1/invitations/{token}/accept — tenant-less on purpose, the
caller is not a member yet so there is no /t/{slug} to resolve into — plus GET for a preview, and the
SPA's /join/:token route: name the community and the role the token grants, accept, land in /t/:slug.
Arriving signed out, the route survives register-and-sign-in (5.6b) and returns to itself.
CONSTRAINT: .claude/rules/auth.md → "Membership lifecycle"; add-endpoint and add-tenant-entity skills;
aegisscribe-design-system skill; the 5.3/5.4 primitives.
RESTRICTION: The token is the ONLY thing that names the tenant. Never accept a tenant id or slug from
the client beside it, or the token for community A becomes a membership in community B — that is the
tenancy rule wearing a friendly hat. Acceptance is single-use and atomic: two concurrent accepts leave
exactly one membership. Expired, consumed and revoked are each refused distinctly, and none of them
reveals the community's name to someone holding a dead token; only a live token's preview names it. An
accept creates a TenantMembership at the token's role and NOTHING else — no RosterEntry, no
CharacterClaim (7.2b is the member's own act, per 8.2's rule). Accepting when already a member is a
no-op that lands them in the community — never a second row, never a silent role change, in either
direction. The token round-trips through the sign-in redirect without being written to localStorage, to
a cookie the SPA sets, or to a log line: it is a bearer secret that grants membership. This screen is
not in design/aegisscribe-armory.html — compose from §05 and §07, invent no tokens. Every accept and
every refusal writes an AuditLog row.
BEHAVIOR: Plan the token's round-trip through register → sign-in → back, and the four refusal
responses, wait for approval, implement, and test: happy path, expired, consumed, revoked,
already-a-member, concurrent double-accept, signed-out arrival, and the two-tenant test — a token for
A grants nothing whatsoever in B.
```

### 8.4 Member management UI
```
SCOPE: member-management screen — invite, review requests, change roles, remove; each member's claimed
character(s) from 7.2b, so officers can see who hasn't claimed one yet.
CONSTRAINT: aegisscribe-design-system skill.
RESTRICTION: Role-conditional UI hides; it never enforces. Destructive actions are outlined, not filled.
BEHAVIOR: Implement, `ng test`, @design-review.
```

### 8.5 First run — the empty community
```
SCOPE: The "you just created this, here's what's next" panel on a community with no guild, no ranks, no
roster and one member: link a guild (8.2) → import the roster → name your ranks (7.1) → invite people
(8.3). Leave the seam for connecting Discord (10.4) without faking the step before it exists.
CONSTRAINT: aegisscribe-design-system skill; §07 chrome; the 5.3/5.4 primitives; .claude/rules/frontend.md.
RESTRICTION: Derive each step's state by asking what the community actually HAS — a persisted
"onboarding step 3 of 4" column drifts the first time an officer deletes a rank, and then lies forever.
It is a checklist, not a wizard: every step is reachable from ordinary navigation and nothing is gated
behind finishing the one before it. Dismissible, and Owner/Officer-only — a plain Member landing in a
fresh community sees the ordinary per-screen empty states, not somebody else's setup list. No step is a
dead end: with no Blizzard credentials the gateways no-op by design (CLAUDE.md → Usage), so the
guild-link step says so plainly instead of failing, and the manual path (7.4's add-a-character) stays
reachable. Not in design/aegisscribe-armory.html — compose from §05 and §07, invent no tokens.
BEHAVIOR: Implement, run `ng test`, @design-review, plus one two-tenant assertion: the panel's state
comes from the ACTIVE community, never from the user's other one.
```

## Phase 8B — Character detail

The profile screen (5.7) draws five tabs, and 6.3 filled only Overview. Progression and Professions
render their panels over an empty array; Specialisations and Collections render a "not synced yet"
placeholder. Each is a separate Blizzard profile endpoint on the character, so each is its own slice.
Run this phase after Phase 8. It extends the Phase 6 Blizzard gateway, so run 6.7 first if it has
not run.

Three decisions hold for every prompt in the phase, and 8B.1 writes them down once:

- **Global zone.** A character's professions, kills, talents and collections are public Blizzard data
  about the character, like `CharacterEquipment`. No `TenantId`, no query filter.
- **Each facet is its own sync.** Its own `LastSyncedAt`, fetched cache-first when its tab is read —
  NOT bundled into the character refresh. Folding all of them into 6.5's refresh would take a
  character from three calls to nine, for tabs most people never open.
- **The 30-day rule still binds each facet.** A stored facet is either refreshed by the worker or
  expired at the deadline; 8B.1 picks which, for all of them.

### 8B.1 Professions ⚑
```
SCOPE: CharacterProfession (global) from /profile/wow/character/{realmSlug}/{characterName}/professions
— primaries and secondaries, one row per profession per skill tier, with skill points and max.
FetchCharacterProfessionsAsync, the cache-first read in the data layer, and
GET /api/v1/characters/{realm}/{name}/professions beside the existing character route.
CONSTRAINT: add-external-sync skill; references/blizzard-endpoints.md; add-endpoint skill for the route;
.claude/rules/api-contract.md — a new route is additive, keep it that way.
RESTRICTION: profile- namespace. Link to Profession (3.2) by Blizzard id when that row exists, but do
NOT require it — the catalogue is not filled until 12.1, and a character's professions must not wait
for it. No TenantId. This prompt SETTLES THE FACET PATTERN for the phase — per-facet LastSyncedAt, lazy
fetch on read, refresh-vs-expire at the 30-day deadline — so record it in .claude/rules/external.md
where 8B.2–8B.5 will read it, rather than in this prompt's code alone. A character with no
professions is an empty list; a character that does not exist is still a 404. Add the table to the
erasure routine — as a note on 14.1 if 14.1 has not run.
BEHAVIOR: Plan the entity, the facet pattern and the refresh-vs-expire choice, wait for approval,
implement, and test with a captured real response: fresh, stale, gateway-down-falls-back, missing, and
no-professions-is-empty. Report the call cost per character.
```

### 8B.2 Raid progression
```
SCOPE: /profile/wow/character/{realmSlug}/{characterName}/encounters/raids, reduced to the current raid
tier: per instance, per difficulty, kills out of total — the shape ProgressionRow already expects —
plus the summary the banner's Raid tile shows (highest difficulty, e.g. 8/9 heroic). Route beside 8B.1's.
CONSTRAINT: the facet pattern from 8B.1; add-external-sync skill; references/blizzard-endpoints.md.
RESTRICTION: The response covers every expansion the character has ever raided. Store what the panel
and the tile need — the current tier — not the history. "Current tier" comes from data (the journal)
or config, NEVER a raid name or instance id written into code; it changes every season. Difficulty
names map once from Blizzard's mode type (LFR, NORMAL, HEROIC, MYTHIC). The response only lists
instances the character has progress in, so a character with no kills this tier has no total to
show — plan whether the total comes from the journal instance or the tab shows an empty state, and
say which.
BEHAVIOR: Plan, wait for approval, implement, test with captured fixtures including a character with
no current-tier kills. Report the call cost.
```

### 8B.3 Mythic+ rating
```
SCOPE: /profile/wow/character/{realmSlug}/{characterName}/mythic-keystone-profile — the current
season's rating, for the banner's Mythic+ tile (value and season, as screen S1 draws it).
CONSTRAINT: the facet pattern from 8B.1; references/blizzard-endpoints.md.
RESTRICTION: One call. Confirm against a captured response that the profile carries the current
rating; if it does, do NOT fetch the per-season detail — nothing on screen needs best runs yet. The
season comes from the response or the season index, never a literal. Blizzard returns a colour with
the rating: do NOT store or render it — the tile is chrome, and a fourth colour system competes with
item quality (frontend.md). No rating this season is "no rating", not zero.
BEHAVIOR: Implement, test with captured fixtures including a character with no rating.
```

### 8B.4 Specialisations
```
SCOPE: /profile/wow/character/{realmSlug}/{characterName}/specializations — the active spec, each
spec's loadouts, and each loadout's talent export string.
CONSTRAINT: the facet pattern from 8B.1; references/blizzard-endpoints.md.
RESTRICTION: This response has changed shape across expansions (the talent tree rework, hero
talents). Read the reference and a CAPTURED response; do not write the mapping from memory, and if
the reference is out of date, say so. Store the export string verbatim — it is the thing a player
actually wants, to paste into the game. Spec names and icons are static game data (12.1b); do not
fetch per-talent media here.
BEHAVIOR: Tell me the response shape you found before writing the entity. Plan, approve, implement,
test with captured fixtures.
```

### 8B.5 Collections
```
SCOPE: /profile/wow/character/{realmSlug}/{characterName}/collections/mounts, /pets and /toys —
counts, and the collected ids.
CONSTRAINT: the facet pattern from 8B.1; references/blizzard-endpoints.md.
RESTRICTION: Three calls and the largest payloads in the phase. Collections are ACCOUNT-wide in the
game, so every alt of one player returns the same list — but an application token cannot tell that
two characters share an account, so do NOT try to dedupe across characters; store per character and
say why in a comment. Store ids, not names or icons: names come from the static mount, pet and toy
indices, synced once globally, never per character. Heirlooms are out of scope. Collections change
slowly, so the longest staleness in the phase is fine — still ≤ 30 days.
BEHAVIOR: Plan, approve, implement, test with captured fixtures. Report the call cost, and the stored
size for a character with a large collection.
```

### 8B.6 Progression and Professions tabs, and the banner tiles
```
SCOPE: character-profile passes real rows to progression-panel and profession-panel instead of [];
character-banner gains the Mythic+ and Raid stat tiles S1 draws beside Item level. New CharacterService
methods for the 8B.1–8B.3 routes.
CONSTRAINT: aegisscribe-design-system skill; screen S1; .claude/rules/frontend.md.
RESTRICTION: ProgressionRow and ProfessionRow are declared on the panels today — move them to
models/character.models.ts with a "// Mirrors" comment, where the api-contract-checker looks. A tab
fetches when it is opened, not when the page loads, matching the lazy facet read. Four states per
panel, and DEGRADED shows the facet's own age — the provenance panel's timestamp is the character's,
not the tab's. No literal hex.
BEHAVIOR: Implement, run `ng test`, then @design-review and @api-contract-checker.
```

### 8B.7 Specialisations and Collections panels
```
SCOPE: design/aegisscribe-armory.html draws the Specialisations and Collections tabs but no panel for
either. Add both panels to the reference first — screen S1, all four states — then build the
components, wire them to the 8B.4 and 8B.5 routes, and remove the "not synced yet" placeholders.
CONSTRAINT: aegisscribe-design-system skill; new-component skill; .claude/rules/frontend.md.
RESTRICTION: Design before code — a component styled without a reference is an invented one. Chrome
tokens only, class colour inherited through --class-color, no new hue. The talent export string gets
a copy control. Item links are the ONLY Wowhead integration — no talent-calculator or mount links
unless frontend.md is extended on purpose first.
BEHAVIOR: Show me the design additions and wait for approval before building components. Then
implement, run `ng test`, then @design-review and @api-contract-checker.
```

### 8B.8 Compliance pass ⚑
```
SCOPE: Audit what Phase 8B built — five new profile reads, the facet refresh or expiry, and erasure
coverage for every new table.
UTILIZATION: @external-compliance, then @test-gap-analyzer.
RESTRICTION: Triage into the same three buckets as 6.7 before fixing anything. Total the calls one
character now costs across every facet, and check the worker's refresh still fits under 36,000/hour
at a realistic character count — state the count you assumed.
BEHAVIOR: Run both agents, triage, fix only bucket (1), re-run, and report the count in each bucket
and the per-character call cost.
```

## Phase 9 — Calendar

### 9.1 Time helper ⚑
```
SCOPE: A single helper converting between UTC and a tenant's IANA timezone, with documented handling of
non-existent and ambiguous local times.
CONSTRAINT: add-tenant-entity skill → references/time-and-recurrence.md.
RESTRICTION: IANA ids only, never Windows ids or raw offsets. Non-existent times shift forward;
ambiguous times take the first occurrence. Decide once, here, not at each call site.
BEHAVIOR: Implement with tests naming a REAL transition date for both cases. This helper is the
foundation of the whole phase — get it right before anything uses it.
```

### 9.2 CalendarEvent and RecurrenceRule
```
SCOPE: CalendarEvent (TenantId, title, StartsAtUtc, duration, signup window, RecurrenceRuleId,
IsCancelled) and RecurrenceRule.
CONSTRAINT: add-tenant-entity skill.
RESTRICTION: Tenant-scoped. Store UTC. Do NOT compute occurrences on read — materialise them (9.3).
BEHAVIOR: Plan, approve, migrate, two-tenant test.
```

### 9.3 Recurrence materialisation
```
SCOPE: Expand a rule into concrete CalendarEvent rows over a bounded window, and a worker job that
extends the window on a schedule.
CONSTRAINT: references/time-and-recurrence.md.
RESTRICTION: Expand in the TENANT's timezone so a weekly slot keeps its local time across a DST
boundary — expanding in UTC silently shifts the raid by an hour twice a year. Never materialise
"forever".
BEHAVIOR: Implement with a test spanning a real DST transition.
```

### 9.4 Editing occurrences and rules
```
SCOPE: Edit one occurrence (detaches it), or the rule with "this and all future". Cancel an occurrence;
delete a rule.
CONSTRAINT: references/time-and-recurrence.md; add-endpoint skill.
RESTRICTION: Editing a rule must NEVER silently rewrite occurrences people have already signed up for.
Deleting a rule does not delete past occurrences — attendance history is a record, not a projection.
BEHAVIOR: Plan the scopes, wait for approval, implement, test that siblings and signups survive.
```

### 9.5 Signups
```
SCOPE: EventSignup (TenantId, EventId, RosterEntryId, State) with the state machine, plus the signup
window lock.
CONSTRAINT: references/time-and-recurrence.md → "The signup state machine"; add-endpoint skill.
RESTRICTION: Signup is per-CHARACTER (via RosterEntry), not per-user. "Their own" means a RosterEntry
with a CharacterClaim (7.2b) naming the caller — a member cannot sign up an unclaimed entry, even one
that's plausibly theirs; claim it first. Officers change anyone's, claimed or not, and every officer
change to someone else's is audited. After lock, officers only.
BEHAVIOR: Implement, test member-after-lock rejected and officer-after-lock permitted with an audit row.
```

### 9.6 Attendance
```
SCOPE: AttendanceRecord (TenantId, EventId, RosterEntryId, Outcome, Source) with manual entry.
CONSTRAINT: references/time-and-recurrence.md → "Attendance derivation"; add-endpoint skill.
RESTRICTION: Attendance is NOT signup — separate entity, recorded after. Source is Manual here;
LogDerived arrives in 11.3 and must never silently overwrite a manual record.
BEHAVIOR: Implement with the two-tenant test.
```

### 9.7 Calendar UI
```
SCOPE: calendar-month and event-detail with the signup panel and attendance panel.
CONSTRAINT: aegisscribe-design-system skill; screen S5.
RESTRICTION: Every displayed time goes through scribe-local-time and is LABELLED with its zone. An
unlabelled raid time is this screen's characteristic bug.
BEHAVIOR: Implement, `ng test`, @design-review.
```

### 9.8 Event composer
```
SCOPE: event-composer for manual create and edit, including recurrence.
CONSTRAINT: aegisscribe-design-system skill.
RESTRICTION: Show the concrete dates a recurrence will produce before saving. AI scheduling comes in
13.3 and reuses this preview — build it so it can.
BEHAVIOR: Implement, `ng test`, @design-review.
```

## Phase 10 — Notifications

### 10.1 Notification entities
```
SCOPE: Notification, NotificationPreference (per user, per tenant, per kind, per channel), and the
NotificationKind enum with template files.
CONSTRAINT: add-notification skill.
RESTRICTION: All tenant-scoped. Kinds are enum members with template FILES, not inline strings.
BEHAVIOR: Plan the kinds and defaults, wait for approval, implement.
```

### 10.2 The raise pipeline
```
SCOPE: An INotificationRaiser called by facades after commit; resolves recipients and preferences at
raise time and writes Notification rows.
CONSTRAINT: add-notification skill.
RESTRICTION: Raise AFTER the transaction commits — a notification for a rolled-back write is worse than
none. Fan out at raise time, not delivery time; the roster may change in between. A per-character
recipient (an unfilled-signup reminder) resolves through the CharacterClaim (7.2b) on that RosterEntry;
an unclaimed entry has no recipient and is silently skipped, never guessed at.
BEHAVIOR: Implement, test that a rolled-back write produces no notification.
```

### 10.3 In-app centre
```
SCOPE: Endpoints for listing, unread count and mark-read, plus notification-centre and the header bell.
CONSTRAINT: add-endpoint, add-tenant-entity, aegisscribe-design-system skills; screen S6.
RESTRICTION: Unread state is a column, not a second table. The centre is the system of record.
BEHAVIOR: Implement, `ng test`, two-tenant test, @design-review.
```

### 10.4 Discord webhook config
```
SCOPE: DiscordWebhook entity, encrypted at rest, with officer-only settings endpoints and a "send test"
action.
CONSTRAINT: add-notification and add-endpoint skills; .claude/rules/external.md → "Discord".
RESTRICTION: The URL is a CREDENTIAL — encrypted at rest, never logged, never returned in full. The
settings screen shows a masked URL and a replace action.
BEHAVIOR: Implement, and add a test asserting the URL never appears in a log or an API response.
```

### 10.5 Dispatch worker
```
SCOPE: NotificationDispatch rows and a worker that posts them to Discord with bounded retry.
CONSTRAINT: add-notification skill.
RESTRICTION: Queued, never inline — a Discord outage must not fail a signup. Respect Discord's
rate-limit headers per webhook. A 401/404 marks the webhook invalid and STOPS retrying, notifying
officers. Retry exhaustion raises SyncFailed.
BEHAVIOR: Implement, test each failure path, then send a real message to a test server.
```

### 10.6 Preferences UI
```
SCOPE: notification-preferences and discord-webhook-settings.
CONSTRAINT: aegisscribe-design-system skill; screen S6.
RESTRICTION: Preferences are per community — the same person can want reminders here and silence
elsewhere. Show per-channel delivery status including failures.
BEHAVIOR: Implement, `ng test`, @design-review.
```

## Phase 11 — WarcraftLogs

### 11.1 Gateway and points budget
```
SCOPE: AegisScribe.Domain/Integration/WarcraftLogs/ — IWarcraftLogsGateway, client-credentials OAuth, GraphQL POST to
/api/v2/client, query documents in files.
CONSTRAINT: add-external-sync skill → references/warcraftlogs.md.
RESTRICTION: Rate limiting is POINTS-based, not request-based — track spend via rateLimitData
(limitPerHour, pointsSpentThisHour, pointsResetIn). Counting requests tells you nothing. A GraphQL
200 with a non-empty `errors` array is a FAILURE. Do not use /api/v2/user.
BEHAVIOR: Plan the points tracking, wait for approval, implement, test the errors-array path.
```

### 11.2 Parse sync
```
SCOPE: CharacterParse entity (global zone) and a worker job to sync parses for rostered characters.
CONSTRAINT: add-external-sync skill.
RESTRICTION: Global — no TenantId. A character with no logs is a normal answer, not an error. Ask for
exactly the fields you need; over-fetching costs points.
BEHAVIOR: Implement with fixtures from captured responses.
```

### 11.3 Log-derived attendance
```
SCOPE: Derive AttendanceRecord rows with Source = LogDerived from report participation.
CONSTRAINT: references/time-and-recurrence.md → "Attendance derivation".
RESTRICTION: A derived record NEVER silently overwrites a manual one — an officer's correction is the
truth, the log is evidence. Log presence can't distinguish "benched but present" from "didn't come";
say so in the UI rather than implying certainty.
BEHAVIOR: Implement, test the no-overwrite rule.
```

### 11.4 Compliance pass
```
SCOPE: Audit the WarcraftLogs and Discord integrations.
UTILIZATION: @external-compliance.
BEHAVIOR: Report every section, fix blockers, re-run.
```

## Phase 12 — Catalogue and AI foundation

### 12.1 Item and recipe sync
```
SCOPE: Sync items, professions, skill tiers and recipes from Blizzard.
CONSTRAINT: add-external-sync skill.
RESTRICTION: Largest volume in the app — page and throttle, and don't chase href links at request time.
Everything a crafting feature needs is in these endpoints; do NOT reach for Wowhead. The wowhead-guard
hook will block it anyway.
BEHAVIOR: Implement, report how many API calls a full catalogue sync costs under the limiter.
```

### 12.1b Media and icons
```
SCOPE: Sync media URLs for items, classes, specs, professions and recipes; character renders via
character-media; and guild crest components (index, border, emblem). Store the URL on the entity, not
the bytes.
CONSTRAINT: add-external-sync skill → references/blizzard-endpoints.md → "Images and media".
RESTRICTION: Blizzard is the ONLY image source — WarcraftLogs hosts nothing renderable and Wowhead is
out of bounds. Never resolve icon filenames against wow.zamimg.com. Do NOT proxy or re-host the
images; the client references render.worldofwarcraft.com directly. The guild-crest INDEX and its MEDIA
sit at different path shapes — check the reference. Refresh media URLs with their entity, not on a
separate schedule. DEDUPE media fetches by icon filename — tens of thousands of items share a far
smaller set of icons, and this is the biggest rate-limit saving in the phase. A media 404 is a NORMAL
answer, not a failure; record "no icon" and continue.
BEHAVIOR: Implement, then show me a character render, an item icon and a composed guild crest on screen.
```

### 12.2 Foundry and Semantic Kernel
```
SCOPE: Add the Foundry resource with separate chat and embedding deployments and RunAsFoundryLocal();
register IChatClient and IEmbeddingGenerator; configure the Kernel.
CONSTRAINT: add-ai-capability skill; .claude/rules/ai.md.
RESTRICTION: Aspire.Hosting.Foundry is PREVIEW and was renamed from Aspire.Hosting.Azure.AIFoundry —
verify at aspire.dev before writing. Nothing outside DI names a provider type. Keep chat and embedding
as separate named resources.
BEHAVIOR: Plan the wiring, wait for approval, implement, confirm `aspire run` still comes up.
```

### 12.3 First plugin
```
SCOPE: CharacterPlugin wrapping ICharacterFacade, with one [KernelFunction].
CONSTRAINT: add-ai-capability skill.
RESTRICTION: Injects a FACADE — no DbContext, repository or gateway. NO tenant id parameter the model
can fill. Write the [Description] for the model, saying when to call it.
BEHAVIOR: Implement with a mocked-facade test, then @ai-guardrails.
```

### 12.4 Embeddings and the vector column
```
SCOPE: SqlVector<float> Embedding on Item with EmbeddingModel and EmbeddedAt, the migration, and a
backfill job in the worker.
CONSTRAINT: add-ai-capability skill → references/vector-search.md.
RESTRICTION: No separate NuGet package — EF Core 10 has it built in. Embeddings generated in the
WORKER, batched, never per request. Record the model and dimension; a mixed-model column returns
confidently wrong neighbours.
BEHAVIOR: Show me the migration before applying. Implement, report backfill time and cost.
```

### 12.5 Semantic item search
```
SCOPE: The vector index migration, a repository query ordering by VECTOR_DISTANCE, the search endpoint,
and the item-search screen.
CONSTRAINT: references/vector-search.md; add-endpoint skill; screen S2.
RESTRICTION: The vector index needs PREVIEW_FEATURES, a clustered PK and 100+ non-null vectors — add it
AFTER the backfill, in its own migration. Start with exact search. Tests use CHECKED-IN FIXED VECTORS
against real SQL Server, never a live model call.
BEHAVIOR: Implement, then let me search "fire resistance cloak for a tank".
```

## Phase 13 — AI features

### 13.1 Crafting advisor
```
SCOPE: Streaming RAG chat over a character's gear and the recipe catalogue, with citations, grounding
disclosure, stop control and the AI badge.
CONSTRAINT: add-ai-capability and add-endpoint skills; screen S4.
RESTRICTION: Empty retrieval produces "I don't know", NOT a model call — this domain has a decade of
outdated content in every model's training set. Blizzard-sourced text is untrusted: fence it. Cache
generated prose against LastSyncedAt.
BEHAVIOR: Implement, test the empty-retrieval path and the cache, then @ai-guardrails.
```

### 13.2 Natural-language roster query
```
SCOPE: The constrained filter object, the LINQ translator, the endpoint, and nl-query-bar with the
interpreted-filter chips.
CONSTRAINT: references/nl-query-safety.md; add-endpoint skill; screen S3.
RESTRICTION: Enums, not strings. Throwing default arm. Per-FIELD authorization against the caller.
Server-side limit clamp. No model output reaches raw SQL anywhere.
BEHAVIOR: Implement, then write EVERY test in the reference's required list — they are security tests,
and a missing one is a blocker.
```

### 13.3 Natural-language scheduling ⚑
```
SCOPE: The constrained CALENDAR COMMAND object, preview expansion, explicit confirmation, and the
idempotent write. Reuses the 9.8 preview.
CONSTRAINT: references/nl-query-safety.md → "The write variant"; add-endpoint skill.
RESTRICTION: This is the riskiest AI feature in the app — a wrong command creates or destroys real
events. The model emits a RULE; server code expands it in the tenant's timezone. Preview concrete rows,
require explicit confirmation, make the write idempotent. Destructive verbs name the exact events and
flag those with signups.
BEHAVIOR: Plan the command shape and the preview/confirm handshake, wait for approval, implement, and
test: preview writes nothing, confirmation required, replay idempotent, stale preview expires, DST
correct.
```

### 13.4 Composition analysis
```
SCOPE: Read a signup list against class/spec capability data and flag gaps — battle rez, tank count,
healer count — on the event detail screen.
CONSTRAINT: add-ai-capability skill; .claude/rules/ai.md.
RESTRICTION: Never state a gap it can't point at a signup for. Show the working.
BEHAVIOR: Implement with a stubbed IChatClient, assert the prompt contains the signups.
```

### 13.5 Attendance insight
```
SCOPE: Generated per-member and per-guild attendance summaries.
CONSTRAINT: .claude/rules/ai.md → "Judgement-shaped output"; add-ai-capability and add-endpoint skills.
RESTRICTION: Describes BEHAVIOUR, not character — "signed up for 4 of the last 12" is a fact,
"unreliable" is a verdict the tool doesn't get to render. Cites the rows. States what it doesn't know.
Is VISIBLE to the person it's about — resolved through their CharacterClaim (7.2b) — or it isn't
built. An unclaimed character's summary is guild-visible only; there is no "the person" to show it to.
BEHAVIOR: Plan the framing carefully and show it to me before implementing — this one is about people,
and the wording is the feature.
```

### 13.6 Recruitment fit and loot guidance
```
SCOPE: RecruitmentApplication entity and endpoints; score an applicant against roster gaps; suggest who
benefits most from a drop.
CONSTRAINT: add-tenant-entity, add-endpoint and add-ai-capability skills.
RESTRICTION: Both are opinions. Each cites its data and presents a ranking as a suggestion, never a
fact. Applications are tenant-scoped and officer-visible.
BEHAVIOR: Implement, two-tenant test, @ai-guardrails.
```

## Phase 13B — The mobile client

> Build this **after** the API is feature-complete and versioned, not alongside it. The MAUI app
> consumes a contract; a contract still moving is a client rewritten every day. Read
> `.claude/rules/mobile.md` and `.claude/rules/api-contract.md` first.

### 13B.1 The contract, and a generated client
```
SCOPE: Regenerate contract/openapi.v1.json from the API, commit it, and generate a typed C# client
from it into src/AegisScribe.Mobile/Generated/. Add a test asserting the committed document matches
what the API produces.
CONSTRAINT: .claude/rules/api-contract.md → "The contract is a committed artefact".
RESTRICTION: Do NOT hand-write model classes for the mobile app — they are generated, which is the
whole reason drift cannot happen on this side. Do NOT edit generated files.
BEHAVIOR: Implement, and show me the test failing when an endpoint changes and passing after
regeneration.
```

### 13B.2 The MAUI shell
```
SCOPE: Create src/AegisScribe.Mobile as a .NET MAUI app targeting iOS and Android, with the API base
URL from build configuration, and navigation for: community picker, roster, calendar, character
detail, notifications.
CONSTRAINT: .claude/rules/mobile.md.
RESTRICTION: Do NOT add it to the AppHost. It is a client, not an orchestrated service, and
`aspire run` does not launch it. No literal API URL. No secrets of any kind.
BEHAVIOR: Plan the project and navigation, wait for approval, implement, and show it building for both
targets.
```

### 13B.2b Landing and registration screen
```
SCOPE: The app's FIRST screen — "Register" and "Log in" entry points, and a native registration form
(display name, email, password → POST /api/v1/auth/register, straight to api.*, per 1.2 and
mobile.md's "talks to api.* directly"). "Log in" launches 13B.3's WebAuthenticator flow.
CONSTRAINT: .claude/rules/mobile.md.
RESTRICTION: Registration is a plain HTTPS POST straight to api.* — no gateway, no cookie, same as
every other mobile call. It does NOT sign the user in; on success, launch the SAME WebAuthenticator
flow as "Log in" (13B.3) rather than fabricating a session from the register response. Do NOT embed a
WebView for either action — sign-in is system-browser only (RFC 8252), same restriction as 13B.3.
BEHAVIOR: Plan the screen and the handoff into 13B.3, wait for approval, implement, and show both
register-then-sign-in and log-in-directly working on one platform.
```

### 13B.3 Authentication ⚑
```
SCOPE: Authorization code + PKCE via WebAuthenticator against the API as client aegisscribe-mobile:
generate verifier and challenge, check state, exchange the code at connect/token, store the refresh
token in SecureStorage. Register the aegisscribe:// callback on both platforms.
CONSTRAINT: .claude/rules/mobile.md → "The auth flow", .claude/rules/auth.md.
RESTRICTION: NEVER an embedded WebView — RFC 8252. WebAuthenticator does NOT do PKCE for you. Do NOT
read result.AccessToken; in a code flow it is null by design — the code is in result.Properties["code"].
Request scope=offline_access or there is no refresh token and the user is signed out after fifteen
minutes. No client secret, obfuscated or otherwise. Refresh tokens go in SecureStorage, never
Preferences and never a file.
BEHAVIOR: Plan the flow and the storage, wait for approval, implement, and demonstrate sign-in on both
platforms plus the token landing in secure storage.
```

### 13B.4 The HTTP layer ⚑
```
SCOPE: One HttpClient with a DelegatingHandler doing attach-token, 401-refresh-retry with
SINGLE-FLIGHT refresh, retry with jitter honouring Retry-After, per-call timeouts, and the
X-Client-Version / X-Client-Platform headers. Handle 426 with a blocking upgrade screen.
CONSTRAINT: .claude/rules/mobile.md → "The HTTP layer".
RESTRICTION: Persist the rotated refresh token BEFORE using it. Distinguish invalid_grant (a real
sign-out) from a network failure (retry, stay signed in) — conflating them logs people out every time
they walk into a lift. Nothing outside this handler builds an Authorization header. Do NOT rely on the
server's 30-second reuse leeway instead of implementing single-flight.
BEHAVIOR: Implement, then test: N parallel 401s produce ONE refresh; a network error mid-refresh does
not sign the user out; a 426 shows the upgrade screen.
```

### 13B.5 Read screens and delta sync
```
SCOPE: Roster, calendar and character detail against the versioned API, with local caching refreshed
by ?since= delta sync including tombstones, and ETag/If-None-Match on collection reads.
CONSTRAINT: .claude/rules/api-contract.md → "Sync is delta-based", .claude/rules/mobile.md.
RESTRICTION: Send back the server's syncedAt, NEVER a device clock reading — a fast device clock
silently skips changes forever. Apply tombstones or a cancelled raid stays on the phone. Clear cached
tenant data on sign-out AND on leaving a community; a roster surviving a removal is a cross-tenant
leak that happens to live on a phone. Deserialization ignores unknown fields; unknown enum values map
to Unknown.
BEHAVIOR: Implement, and test that a deletion on the server disappears from the phone on next sync.
```

### 13B.6 Signups, idempotency and push
```
SCOPE: Raid signup from the app with an Idempotency-Key; device registration for push (user + tenant +
platform token) and the push delivery channel in the sync worker.
CONSTRAINT: .claude/skills/add-notification/SKILL.md, .claude/rules/mobile.md → "Push notifications";
add-endpoint skill for the server-side device-registration route.
RESTRICTION: Generate the Idempotency-Key ONCE per user intent and reuse it across retries —
regenerating per attempt is the same as not sending it. Push payloads carry NO private content: a lock
screen is public, so send "New event in Emberfall" plus an id, never an officer note. Device
registrations are tenant-scoped and unregistered on sign-out and on leaving a community. Push is a
channel on the EXISTING pipeline — do NOT build a parallel one, or a muted notification stops being
muted.
BEHAVIOR: Implement, and test: a double-submitted signup creates one row; a push payload contains no
private field; leaving a community stops its pushes.
```

### 13B.7 Mobile verification
```
SCOPE: Prove the mobile edge.
UTILIZATION: @code-reviewer, @api-contract-checker.
BEHAVIOR: Run and show me all six: (1) no secret anywhere in the built app; (2) sign-in uses the system
browser, not a WebView; (3) a token request without a PKCE verifier is rejected; (4) parallel 401s
produce one refresh; (5) sign-out revokes the refresh token — replaying it fails; (6) a member of
community B cannot reach community A's roster from the app.
```

---

## Phase 14 — Erasure, audit and close

### 14.1 Erasure and tombstones
```
SCOPE: ICharacterDataDeletionFacade and its Business/DataLayer in AegisScribe.Domain, plus both
trigger routes as API controllers — the erasure path with the cross-tenant delete, the
SyncSuppression tombstone and cache invalidation.
CONSTRAINT: .claude/rules/external.md → "The deletion path, concretely"; add-endpoint skill.
RESTRICTION: This is ONE OF THE TWO sanctioned IgnoreQueryFilters uses — comment it as such. A plain
DELETE without a tombstone is undone by the next sync. Enumerate tables explicitly; reflection gives
false assurance.

The tombstone only works if the sync paths CHECK it, and those already exist. Every one of these has
to learn about SyncSuppression, and 6.7's compliance pass recorded them here so they are not
rediscovered by grep:
  - CharacterRepository.FindStaleAsync — the worker's stale-row query (6.5) must exclude suppressed
    source ids, or the next pass re-fetches the character an hour after erasure.
  - CharacterDataLayer.GetCharacterAsync / RefreshCharacterAsync — the cache-first and forced reads
    (6.4, 6.6) must refuse a suppressed character rather than fetching it, and return "not available"
    rather than an empty profile.
  - GuildSyncDataLayer.SyncAsync — a roster contains its members, so a guild sync (6.6b) will happily
    recreate a suppressed character's row from the roster alone. Skip suppressed members.
  - GuildRepository.FindStaleAsync — the guild refresh pass (6.6b) reaches the same path.
Entities carrying a Blizzard source id at the time of writing: Character, CharacterEquipment and
EquippedItem (via CharacterId), Realm, Guild, GuildMember (via CharacterId), Item.
BEHAVIOR: Implement, and write the test that every entity with a source id appears in the routine.
```

### 14.2 Audit log surfacing
```
SCOPE: An officer-visible audit log screen for the tenant.
CONSTRAINT: add-tenant-entity, add-endpoint and aegisscribe-design-system skills.
RESTRICTION: Tenant-scoped, TenantOfficer. Read-only — audit rows are never editable or deletable from
the app.
BEHAVIOR: Implement, two-tenant test, @design-review.
```

### 14.3 Full audit sweep
```
SCOPE: Audit the entire codebase for release readiness. Report first; fix nothing yet.
UTILIZATION: @tenant-isolation-auditor, @external-compliance, @ai-guardrails, @design-review,
@skills-evals, @code-reviewer, @api-contract-checker, @test-gap-analyzer.
RESTRICTION: Report only findings you can point at with a file and line.
BEHAVIOR: Give me ONE consolidated list, deduplicated across agents, grouped by severity, with
tenant-isolation and external-compliance blockers first. Then wait — I'll say what to fix.
```

### 14.4 End-to-end tests
```
SCOPE: Playwright coverage for the critical journeys: look up a character; register, create a community
and invite a member; build a roster with ranks and claim a character; schedule a raid and sign up as
your claimed character; receive the Discord notification; ask the advisor a question; run a
natural-language roster query.
CONSTRAINT: playwright-cli skill.
RESTRICTION: Include a TWO-TENANT journey — sign in as a member of community B and confirm community
A's roster and calendar are unreachable. That is the one that matters most.
BEHAVIOR: Implement, run green, report.
```

### 14.5 Hardening and ship
```
SCOPE: Fix the remaining findings from 14.3, confirm every resource healthy, and give me the release
summary.
RESTRICTION: Fix only what the audits flagged plus the e2e gaps. No new features, no opportunistic
refactors. If a finding needs a design decision, stop and ask.
BEHAVIOR: Final report: each resource's health, what you fixed, what you deliberately didn't and why,
and the remaining prioritized test gaps.
```

---

# Part 2 — Operational templates

Copy a block, fill the `<...>`, delete lines that don't apply.

## Template A — Feature slice
```
SCOPE: Deliver <feature> end to end: <API change> and <UI change>. This feature only.
CONSTRAINT: The rules in .claude/rules/. Match existing patterns rather than inventing new ones.
RESTRICTION: Do NOT change unrelated files, schemas or ServiceModel contracts. No new dependencies
without asking. No hardcoded config. No literal hex in the frontend. If it touches tenant-scoped data,
it owes a two-tenant test.
UTILIZATION: add-endpoint / add-tenant-entity / new-component + aegisscribe-design-system skills.
Review with @code-reviewer and @design-review; add @tenant-isolation-auditor if tenant-scoped.
BEHAVIOR: Plan the slice (zone → data → API → UI), wait for approval, implement in small steps, run
`dotnet test` and `ng test` green, run the reviewers, summarize.
```

## Template B — Migration
```
SCOPE: Make this schema change: <describe>. Produce the migration and update models, queries and tests.
CONSTRAINT: .claude/rules/backend.md, tenancy.md.
RESTRICTION: Create the migration but do NOT apply until I approve. State the zone of any new entity
and why. If it's tenant-scoped it needs TenantId, the filter, TenantId-leading indexes and a two-tenant
test. If it touches an externally-derived table, the erasure routine and its test change too. Changing
an embedding model or dimension is a migration PLUS a full re-backfill.
UTILIZATION: Plan mode; @code-reviewer, plus @tenant-isolation-auditor and @external-compliance where
relevant.
BEHAVIOR: Show me the model change, the generated migration and the rollback story. Wait. Then apply,
confirm has-pending-model-changes is clean, run tests.
```

## Template C — Refactor
```
SCOPE: Refactor <target> to <goal>. Behavior must not change. List every file you'll touch first.
CONSTRAINT: The rules in .claude/rules/. Keep ServiceModel contracts stable.
RESTRICTION: No behavior change, no unrelated edits, no expansion beyond the list without checking in.
Do NOT "simplify" the ExecuteInTransactionAsync callback to begin/commit — the retry execution strategy
refuses a caller-opened transaction and it breaks at runtime. Do NOT remove a query filter or a
pass-through data-layer method because it "does nothing".
UTILIZATION: Explore subagent to map usages; @code-reviewer and @skills-evals after.
BEHAVIOR: Return the impact map, wait for approval, refactor in small test-green steps. If the blast
radius grows beyond the map, stop and re-plan.
```

## Template D — Debug
```
SCOPE: Diagnose and fix <bug>, add a regression test.
CONSTRAINT: The rules in .claude/rules/.
RESTRICTION: MINIMAL change to the root cause. Do NOT refactor around it or suppress the symptom.
UTILIZATION: Explore subagent to locate; aspire-monitoring skill for logs and traces;
@test-gap-analyzer after.
BEHAVIOR: Reproduce first and state your root-cause hypothesis with evidence. Wait for my nod on the
diagnosis. Then fix, add the regression test, run the suite, summarize.
```

## Template E — New external data
```
SCOPE: Sync <data> from <Blizzard | WarcraftLogs> end to end: gateway method, entity, migration,
cache-first read, worker job, erasure entry, seed data, tests.
CONSTRAINT: add-external-sync skill. Read the source's reference before writing anything.
RESTRICTION: GLOBAL zone unless there's a stated reason otherwise — per-tenant syncing multiplies API
calls by tenant count. LastSyncedAt and a source id, indexed. Rate-limiter lease (points, for
WarcraftLogs). Bounded concurrency. Add the table to the erasure routine and its test. Refresh ≤ 30
days. No Wowhead.
UTILIZATION: @external-compliance.
BEHAVIOR: Confirm the endpoint/query and namespace against the reference and tell me which before
writing. Plan, approve, implement, test, report all compliance sections.
```

## Template F — New tenant-scoped feature
```
SCOPE: Add <feature> as tenant-scoped data: <entities>, endpoints, UI.
CONSTRAINT: add-tenant-entity and add-endpoint skills; .claude/rules/tenancy.md.
RESTRICTION: Justify the zone with the "would two communities disagree about it?" test before writing.
TenantId + filter by convention + TenantId-leading indexes + tenant-prefixed cache keys + membership
policy. No global entity may hold an FK to it. Officer actions on others' data are audited.
UTILIZATION: @tenant-isolation-auditor, then @code-reviewer.
BEHAVIOR: State the zone decision and defend it, wait for approval, implement, and write the
two-tenant test through the ENDPOINT before you call it done.
```

## Template G — Ship gate
```
SCOPE: Audit for release readiness. Report only; fix nothing yet.
CONSTRAINT: Read each subagent's named rule sources before auditing. Do not audit from memory.
RESTRICTION: Only findings you can point at with a file and line. Anything unverifiable with Read/Grep
goes under "Not verifiable here".
UTILIZATION: @tenant-isolation-auditor, @external-compliance, @ai-guardrails, @design-review,
@skills-evals, @code-reviewer, @api-contract-checker.
BEHAVIOR: One consolidated list, deduplicated, grouped by severity, tenant-isolation and
external-compliance blockers first. Then wait.
```

---

## Pro tips

- **Approve the plan, not the code.** Catching a wrong approach before it exists is the whole point.
- **Any prompt that adds a route owes the add-endpoint skill**, whether or not its CONSTRAINT line
  names it — auth, `/me` and diagnostics included. A controller in `AegisScribe.ApiService`, every
  layer below it in `AegisScribe.Domain`. The CONSTRAINT line is what gets loaded; a prompt that cites
  only a rules file is how the stack got skipped the first time.
- **Phase 2 and prompt 9.1 deserve extra scrutiny.** Tenancy and the time helper are both foundations
  whose mistakes surface much later, somewhere else, looking like something different.
- **One prompt, one clean context.** `/clear` between phases.
- **The auditor is not a formality.** `@tenant-isolation-auditor` is the only thing standing between
  you and a silent cross-community leak; a single-tenant test suite will never catch one.
- **Compliance findings are not nits.** They're contractual. A rate limit you didn't bother with is how
  an API key gets revoked.
- **Use `/rewind`** instead of stacking corrections on a polluted context.
- **Promote repeats to skills.** Fill in the same template three times and that shape wants to be a
  skill. Write it, and the prompt disappears.
