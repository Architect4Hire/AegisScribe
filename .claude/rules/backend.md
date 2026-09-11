---
paths:
  - src/AegisScribe.ApiService/**
  - src/AegisScribe.MigrationService/**
  - src/AegisScribe.SyncWorker/**
---
# Backend rules — ASP.NET Core + EF Core (Aspire, SQL Server)

**Read `.claude/rules/tenancy.md` first.** Every entity below belongs to exactly one of two zones —
global reference or tenant-scoped — and that placement decides whether it carries `TenantId`, whether
it gets a query filter, and how its cache key is built. Nothing else in this file makes sense until
that's settled.

- **Controllers, not minimal API route mapping.** Every HTTP endpoint in the API — feature routes,
  Identity glue, and OpenIddict's own protocol surface (`connect/authorize`, `connect/token`) alike —
  is an MVC controller action under `Controllers/`, using constructor-injected dependencies. No
  `app.MapGet`/`MapPost`/`MapMethods` for anything beyond what `AddServiceDefaults()`'s
  `MapDefaultEndpoints()` already provides. This isn't just a style preference: minimal API's
  `RequestDelegateFactory` infers whether a complex-type parameter is a service or a request body by
  position and type, and a service type it doesn't specifically recognise (e.g. a second
  `SignInManager<T>` parameter following a `UserManager<T>` one) gets misread as an inferred body —
  which throws `InvalidOperationException` at **startup**, crashing the whole host, not at the call
  site. Controllers don't have this failure mode, because DI happens unambiguously through the
  constructor. OpenIddict's own samples use controllers for exactly this reason; follow that
  convention rather than porting a minimal-API sample verbatim.
- **Thin controllers.** Bind the ViewModel, call the facade, return a typed result. No business
  logic, validation, caching, or EF queries in controllers. `[Authorize(Policy = ...)]` on the
  action *is* allowed — it's declarative HTTP metadata, not logic. Tenant-scoped routes sit under
  `/api/v{version}/t/{tenantSlug}/...` and carry a tenant policy. The version segment comes first and
  is not optional — see `.claude/rules/api-contract.md`.
- **ViewModels in, ServiceModels out.** Never return an EF entity from an endpoint. There is no
  separate DTO layer: the domain entity is the internal shape.
- **The layer stack is fixed.** `Controller → Facade → Business → DataLayer → Repository | Gateway`.
  Each layer depends on the **interface** of the one below it, never a concrete class. Full
  responsibilities, folder layout, and the reasoning behind each seam live in
  `.claude/skills/add-endpoint/SKILL.md` — read it before adding or changing an endpoint.
- **DbContext via Aspire.** Register through the Aspire SQL Server EF Core integration keyed to the
  AppHost database resource name (`builder.AddSqlServerDbContext<AegisScribeDbContext>("aegisscribedb")`),
  not by reading a raw connection string from `appsettings.json`. Retries and health checks come
  from the integration; don't re-implement them.
- **Async all the way.** `async Task<...>` with `await`; never `.Result` or `.Wait()`. Pass the
  `CancellationToken` down every layer — a character sync that the client abandoned should stop.
- **Validate at the edge.** FluentValidation on write ViewModels; on failure the global handler
  returns the shared `ProblemDetails` shape, not a raw exception.
- **Service defaults.** `Program.cs` calls `AddServiceDefaults()` so telemetry, health checks, and
  resilience match the rest of the system.
- **EF Core workflow.** Change the model, then `dotnet ef migrations add <Name>`, review, then
  `dotnet ef database update`. Commit the migration. Confirm
  `dotnet ef migrations has-pending-model-changes` is clean.
- **Naming.** PascalCase types/methods, `_camelCase` private fields, camelCase locals.

## The API's public edge

The API has its own hostname (`api.*`) because two different clients need it there: the **mobile app**
calls it directly, and ops curls it. That makes it a genuine internet-facing service, and the rules
below are the price.

**No CORS policy. None.** This survives mobile arriving, and it is worth understanding why rather
than assuming it lapsed. A native app has no origin and makes no preflight — **CORS is a browser
mechanism, and the mobile client is not a browser.** So the absence still does exactly what it did:
no browser anywhere can read an API response cross-origin, even holding a stolen token. The SPA talks
to the BFF; the mobile app is unaffected. **Adding a permissive CORS policy "just to test something"
removes that protection**, and it remains the single most likely way this edge gets weakened. If you
need to call the API from a browser during development, use a REST client, not the app.

The one thing that *would* need CORS is a browser-based sign-in redirect — and it doesn't, because
`connect/authorize` is a top-level navigation, not a fetch.

**Rate limiting is per client, not just per IP.** A public hostname is scanned within hours of DNS
propagating, and a mobile fleet makes the naive version worse: thousands of users behind one carrier
NAT share an IP, so a per-IP bucket tight enough to stop an attacker throttles a whole city.
Partition on `sub` for authenticated routes, on `client_id` for a fleet-wide budget, and on IP only
for anonymous ones. Return 429 with `Retry-After` — the mobile client honours it
(`.claude/rules/mobile.md`), and a limiter without `Retry-After` invites the retry storm it exists to
prevent. Character lookup is anonymous and cheap to abuse; it needs the tightest bucket.

**The token endpoint is public, and protected by OAuth rather than by obscurity.** This changed when
mobile arrived: `connect/token` can no longer be gated behind a gateway credential, because a public
client has no credential to present. What protects it instead is the protocol — PKCE binds an
authorization code to the app that requested it, codes are single-use and short-lived, and refresh
tokens rotate with reuse detection. See `.claude/rules/auth.md`. **Do not add a shared secret to the
mobile client to restore the old gate**; a secret in a shipped app is not a secret, and it would buy
nothing PKCE isn't already buying.

**Versioning is mandatory, from the first endpoint.** `/api/v1/...`. A phone cannot be redeployed, so
an unversioned route is a promise you can't keep. Full rules — and the list of what counts as
breaking — in `.claude/rules/api-contract.md`.

**OpenAPI is non-production only**, and the document is **committed** at `contract/openapi.v1.json`.
Consumers are first-party, so a public schema in production is free reconnaissance for no benefit;
but the mobile client is generated from that document, so it has to be a reviewable artefact in the
repo rather than something that only exists at runtime.

Everything else about the API is unchanged by being public: **the token is the security boundary**,
and it was already doing that work when the gateway was the only caller. Being reachable is not being
unprotected.

## The transaction shape is forced, not chosen

Aspire's SQL Server integration enables retry-on-failure, and EF Core's execution strategy refuses
to run inside a transaction the caller opened itself — it can't replay a unit whose boundaries it
doesn't own. So a multi-write operation is handed to the repository **as a callback**:

```csharp
await _repository.ExecuteInTransactionAsync(async token => { /* the whole operation */ }, ct);
```

Not `BeginTransactionAsync`. Two consequences: the callback **may run more than once**, so it must
be safe to repeat; and only work done through this repository rolls back, so an outbound Blizzard
call or a model call belongs *outside* it.

## Domain shape

**Global reference zone** — public external data, no `TenantId`, no query filter:

`Realm` · `Character` (realm, name, level, class, spec, item level, faction, `LastSyncedAt`) ·
`CharacterEquipment` / `EquippedItem` · `Item` (Blizzard item id, quality, slot, item level,
`SearchText`, `Embedding`) · `Profession` / `Recipe` / `ReagentSlot` · `Guild` /
`GuildMember` (with `BlizzardRank`) · `CharacterParse` (WarcraftLogs) · `SyncSuppression`.

**Tenant-scoped zone** — `TenantId` plus a global query filter, always:

`Tenant` (slug, name, timezone) · `TenantMembership` · `TenantRank` (the community's own rank ladder)
· `RosterEntry` (links a `Character` into a tenant with its `TenantRankId`, notes, join date, and
`MainCharacterId` for alt linking) · `CalendarEvent` · `EventSignup` · `AttendanceRecord` ·
`Notification` / `NotificationPreference` · `DiscordWebhook` · `DeviceRegistration` · `CharacterClaim` ·
`RecruitmentApplication` · `AuditLog`.

Three shapes here are load-bearing and easy to get wrong:

- **`Character` is global; `RosterEntry` is tenant-scoped.** One character row, N roster entries. This
  is what stops tenant count from multiplying Blizzard API calls.
- **`GuildMember.BlizzardRank` and `RosterEntry.TenantRankId` are different things.** The first is
  what the game says (0–9, from the roster endpoint). The second is what the community says
  ("Raider", "Trial", "Social"). Neither derives from the other.
- **Alt linking lives on `RosterEntry`**, not on `Character` — because who is somebody's main is a
  community's judgement, and the same player may be organised differently in two communities.

Two fields carry compliance weight and are never optional on a global externally-derived entity:
**`LastSyncedAt`** (the 30-day refresh obligation is enforced against it) and a stable **source id**
(deletion requests are executed against it). A synced entity without both is incomplete — see
`.claude/rules/external.md`.

## The sync worker and migration service

They live under the same rules. The sync worker is a `BackgroundService` that owns no HTTP surface,
reaches external APIs only through their gateways, and writes only through the repositories. It is one
of the two places allowed to run cross-tenant — its notification-dispatch and per-tenant digest jobs
legitimately iterate tenants, and each such loop sets the ambient tenant explicitly for the work it
does inside, rather than disabling the filter wholesale.

The migration service applies migrations once through an execution strategy and exits:

```csharp
var strategy = db.Database.CreateExecutionStrategy();
await strategy.ExecuteAsync(async () => await db.Database.MigrateAsync(ct));
```
