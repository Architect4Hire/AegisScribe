# AegisScribe

*Project memory, written as a SCRUB prompt — Scope, Constraints, Restrictions, Usage, Behavior. Loaded every session. Every new rule has one obvious home, and every misstep is diagnosable by section.*

## Scope

AegisScribe is a **multi-tenant character armory and guild-operations platform for World of Warcraft**. A community signs up, links its guilds, and gets: every member's characters with gear and progression, a roster with its own ranks, a raid calendar with signups and attendance, notifications where the guild actually lives, and an AI layer that reads all of it — what to craft, who's missing from Tuesday's comp, who's drifting, whether an applicant fits.

It's built to showcase real-world skills with **Aspire + ASP.NET Core + Angular + SQL Server**, developed with Claude Code. It does two things at once: it's a genuinely useful product, and a public demonstration of driving that stack agentically. The reusable toolkit lives in `.claude/`.

**The name.** *Aegis* is the shield of Athena — the thing that carries a device and protects the bearer. *Scribe* is the one who keeps the record. Between them they name what this is: a register of what people bear. It borrows no Blizzard mark, which matters, because Blizzard's Developer API Terms of Use forbid an application whose title or URL contains one. Do not rename this project to anything containing *Warcraft*, *WoW*, *Azeroth*, *Battle.net*, *Blizzard*, or an expansion name. See Restrictions.

**AegisScribe is also the name of the design system.** The UI reference lives at `design/aegisscribe-armory.html` — a standalone page defining the tokens, components and screens. It is the source of truth for anything visual; see `.claude/skills/aegisscribe-design-system/SKILL.md`.

- **In bounds:** the platform and the `.claude/` toolkit that builds it.
- **Out of bounds (don't build unprompted):**
  - **Battle.net OAuth / "sign in with Battle.net."** Every character page this app shows is reachable with an *application* token (client credentials). A user access token buys only the signed-in player's own account view. That's a separate layer with its own consent and scope story. Auth here is ASP.NET Core Identity with local accounts, plus tenant membership.
  - **A third-party developer API.** Consumers are first-party — the web app, the mobile app, and ops tooling. No client registration UI, no scopes beyond the three registered clients, no developer portal. The seams are there if that changes; nothing implements it.
  - **Offline writes on mobile.** The app caches for reading. A queued signup submitted an hour later against a raid that has since filled is worse than being told the network is down; offline writes need conflict resolution designed on purpose.
  - **i18n / multi-language UI.** Blizzard's `locale` query parameter fetches English strings; that is not a translation layer.
  - **Email delivery.** Notifications go to the in-app centre, Discord and mobile push. Email means a sender domain, deliverability work and unsubscribe handling — a deliberate later decision, not an assumption.
  - **Billing, plans and metering.** The tenancy model leaves room for it; nothing implements it yet.

## Constraints

**Stack**

- **Orchestration:** Aspire 13 (AppHost + ServiceDefaults) on .NET 10
- **Backend:** ASP.NET Core Web API · EF Core 10 (SQL Server) — the HTTP host in `src/AegisScribe.ApiService/`, every layer below the controller in `src/AegisScribe.Domain/`
- **Tenancy:** community-as-tenant, shared database, `TenantId` discriminator + global query filter
- **Auth:** **OpenIddict** on the API issues tokens (authorization code + PKCE, rotating refresh). The browser holds a cookie against the gateway; the **mobile app holds tokens directly**. ASP.NET Core Identity for identity, **tenant membership** for authorization
- **AI:** Microsoft.Extensions.AI abstractions (`IChatClient`, `IEmbeddingGenerator`) over **Azure AI Foundry**, with **Semantic Kernel** for plugins and orchestration
- **Vector search:** SQL Server 2025 native `VECTOR` columns via EF Core 10 — the same database, not a second store
- **Frontend:** Angular (standalone components, strict TS, `scribe-` component prefix) — `src/web/`, served by `AegisScribe.Web`
- **Mobile:** .NET MAUI — `src/AegisScribe.Mobile/`. An OAuth **public client** calling `api.*` directly; not an Aspire resource
- **Edge:** YARP 2.3 reverse proxy in `AegisScribe.Gateway` — the BFF. Three public hostnames on one registrable domain: `app.*` (SPA), `bff.*` (gateway, the browser's only door), `api.*` (the API itself — the mobile app's door, and ops access)
- **API contract:** URL-segment versioning from the first endpoint (`/api/v1/...`). A shipped mobile app cannot be redeployed, so the contract is a promise with a long tail — `.claude/rules/api-contract.md`
- **Data/infra (local containers via Aspire):** SQL Server 2025, Redis cache, Foundry Local
- **External data:** the Blizzard Game Data + Profile APIs and the **WarcraftLogs API v2**, both server-side and synced into SQL; **Discord webhooks** outbound; Wowhead client-side links and tooltips only — see Restrictions

**Layout**

```
src/
├── AegisScribe.AppHost/           # Aspire orchestrator — declares every resource
├── AegisScribe.ServiceDefaults/   # shared telemetry, health checks, resilience, discovery
├── AegisScribe.Gateway/           # YARP BFF — public at bff.*, owns the session; an OAuth confidential client
├── AegisScribe.Web/               # thin host serving the Angular bundle — public at app.*
├── AegisScribe.ApiService/        # HTTP host: controllers + OpenIddict + auth policies + tenant middleware — public at api.*
├── AegisScribe.Domain/            # Facade → Business → Data (DbContext, repositories, migrations) + Managers
│                                  #   (models, validators, mappers) + Context + Integration + Ai — referenced
│                                  #   by the API, sync worker and migration service; references none of them
├── AegisScribe.Mobile/            # .NET MAUI client — OAuth public client, not orchestrated by Aspire
├── AegisScribe.SyncWorker/        # Blizzard + WarcraftLogs sync, embedding backfill, notification dispatch
├── AegisScribe.MigrationService/  # applies EF migrations once, before the API starts
├── AegisScribe.Tests/
└── web/                           # Angular source — built bundle is served by AegisScribe.Web

design/
└── aegisscribe-armory.html        # the design system + screen reference (standalone, no build)

docs/
├── architecture.md                # high-level architecture + key decisions — narrative, not a rule source
└── scrub-prompts.md               # the seam-by-seam build sequence — narrative, not a rule source
```

**Architecture conventions** — area detail auto-loads from `.claude/rules/` (`tenancy.md`, `aspire.md`, `backend.md`, `auth.md`, `gateway.md`, `api-contract.md`, `mobile.md`, `external.md`, `ai.md`, `frontend.md`). The essentials:

- **Tenancy comes first.** Read `.claude/rules/tenancy.md` before anything else. Two zones: **global reference** data (characters, items, recipes — public Blizzard data, no `TenantId`) and **tenant-scoped** data (rosters, ranks, events, notifications — `TenantId` plus a global query filter). One global `Character` row, N tenant `RosterEntry` rows pointing at it.
- **Aspire:** every resource is declared in the AppHost. The SQL Server image is pinned to a **2025** tag — the default 2022 image has no `VECTOR` type and the AI features silently have nowhere to live.
- **Backend:** the layered stack is `Controller → Facade → Business → DataLayer → Repository|Gateway`, and it applies to **every** endpoint — auth, `/me` and the user lookups behind OpenIddict included. Controllers live in `AegisScribe.ApiService` and inject only facades; everything below them lives in `AegisScribe.Domain`, which never references the API or any HTTP type. Thin controllers, ViewModels in and ServiceModels out, everything async, input validated at the edge, EF entities never crossing the boundary. Full detail in `.claude/skills/add-endpoint/SKILL.md`.
- **Auth:** two front doors, one resource server. **OpenIddict** on the API issues tokens; the gateway runs the code+PKCE flow as a confidential client and keeps the tokens server-side, handing the browser only an `HttpOnly` cookie, while the **mobile app is a public client holding tokens on the device**. The API validates a bearer token and nothing else — no cookies, no sessions. Identity roles are **platform-level only**; tenant authorization is membership-based (`Member` / `Officer` / `Owner`) through tenant-aware policies. Resource rules that need to read data stay in Business.
- **AI:** every model call goes through `IChatClient` / `IEmbeddingGenerator`; Semantic Kernel plugins call the **existing facades**, never the `DbContext`, and never take a model-supplied tenant id. The model never emits SQL — see Restrictions.
- **External data:** Blizzard and WarcraftLogs are reached only through gateways in `AegisScribe.Domain/Integration/`, behind rate limiters, and their answers are persisted. Blizzard sync is **global**; tenant-triggered syncs draw on a per-tenant budget. Wowhead is never called from the server at all.
- **Frontend:** standalone components, typed models mirroring ServiceModels, HTTP only through services, `withCredentials` for the auth cookie, `async` pipe. Everything visual comes from the design system — tokens are copied from `design/aegisscribe-armory.html`, never invented, and a literal hex under `src/web/src/app/` is a defect.

**One database, one context.** Identity tables, global reference tables and tenant tables share `AegisScribeDbContext` (in `AegisScribe.Domain/Data/`) and one migration history (in `AegisScribe.Domain/Migrations/`). A tenant-scoped write frequently touches an Identity user and a domain row together; splitting the context would make that a coordination problem for no benefit.

**Canonical commands** (use these verbatim)

- Whole system (repo root or the AppHost folder): run everything + dashboard `aspire run` · add a resource package `aspire add <resource>`
- Backend (repo root): `dotnet test` · `dotnet ef migrations add <Name> --project src/AegisScribe.Domain --startup-project src/AegisScribe.ApiService` · `dotnet ef database update --project src/AegisScribe.Domain --startup-project src/AegisScribe.ApiService`
- Frontend (`src/web/`): `npm install` · `ng test` · `ng build`

## Restrictions

**Tenant isolation is the highest-severity rule in this repo.** A missed query filter doesn't crash, doesn't log, and passes every single-tenant test — it just shows one community another community's data. `.claude/rules/tenancy.md` holds the full rules; the non-negotiables:

- **Never accept a `TenantId` from the client.** It is resolved from the route and validated against membership.
- **Every tenant-scoped entity carries `TenantId` and a global query filter.** Every global reference entity carries neither.
- **`IgnoreQueryFilters()` is banned** outside the migration service, sync worker, and erasure routine — and those must say in a comment which case they are.
- **Tenant-scoped cache keys are prefixed with the tenant; global keys are not.**
- **Every tenant-scoped feature ships a two-tenant isolation test.** A single-tenant suite proves nothing.
- **Unknown-tenant access returns 404, not 403.** A 403 confirms the tenant exists.

**The gateway is the browser's only door; the API is publicly addressable for everything else.** Three public hosts — `app.*` (SPA), `bff.*` (gateway), `api.*` (API) — and the split is a security boundary, not just a deployment convenience:

- **The SPA never calls `api.*` directly.** The public API hostname exists so you can curl production and diagnose an incident, not as a path for the front end. Anything in `src/web/` pointing at `api.*` is a bug.
- **The API has no CORS policy, ever.** No browser should make a cross-origin request to it, and the absence of CORS headers is what stops a malicious page reading its responses even with a stolen token. Adding a permissive policy "to test something" is the most likely way this edge gets weakened.
- **The API is rate-limited independently.** A public hostname is scanned within hours; anonymous character lookup needs the tightest bucket.
- **The token endpoint is public, and protected by the protocol, not by obscurity.** A public mobile client has no secret to present, so PKCE, single-use codes and rotating refresh tokens are the control. **Do not add a client secret to the mobile app** to restore the old gate — a secret in a shipped binary is not a secret.
- **Every route is versioned.** `/api/v1/...`, from the first endpoint. An unversioned route is a defect, because a phone cannot be redeployed. What counts as a breaking change is in `.claude/rules/api-contract.md`.
- **The mobile app never becomes a precedent for the SPA.** Tokens on a device are correct; a token in `localStorage` is not.
- **OpenAPI is non-production only.** There are no third-party consumers planned, so a public schema in production is free reconnaissance.
- **The browser never receives a token.** The cookie is `HttpOnly`; the gateway holds the tokens server-side. No token in `localStorage`, no `Authorization` header built in TypeScript, no refresh timer.
- **Mobile sign-in uses the system browser**, never an embedded `WebView` (RFC 8252). Refresh tokens live in platform secure storage; sign-out revokes before clearing.
- **The gateway strips client-supplied `Authorization` and `X-Forwarded-*` headers** before setting its own. Without that, a caller supplies their own bearer token and the boundary is decorative.
- **`app.*` and `bff.*` must stay subdomains of one registrable domain.** That pair carries the cookie, and same-site is what keeps it viable; genuinely different domains would force `SameSite=None`, which Safari's ITP blocks. The cookie carries `Domain=.aegisscribe.com`, so **no untrusted content may ever be hosted on any subdomain**.
- **CORS names the SPA origin explicitly, from config, with credentials.** `AllowAnyOrigin()` with `AllowCredentials()` is invalid, and reflecting the caller's origin is the same as having no CORS.
- **Tokens are RS256/ES256 with a published JWKS**, short-lived, carrying `sub` and nothing tenant-shaped. Symmetric signing would let any validator forge.

**Blizzard's Developer API Terms of Use are binding, and several of them are architectural.**

- **Attribution, and no implied endorsement.** The UI identifies Blizzard as the source of the data, without suggesting Blizzard endorses or is affiliated with this app.
- **No Blizzard trademark in the application title or URL.** See Scope.
- **Refresh cached data at least every thirty days.** The staleness policy is therefore a compliance control, not a caching tunable — nothing may be configured with a TTL longer than 30 days.
- **Honour deletion requests.** There is a path to delete every stored row derived from an individual's data, across every tenant.
- **No advertising, promotion, or resale** of Blizzard data.
- **Bearer header only.** `Authorization: Bearer <token>` — passing the token as `?access_token=` was disallowed in 2024 and will fail.
- **Rate limit as if the cap were real, because it is.** 36,000 calls/hour is contractual. Blizzard sync is global precisely so tenant count doesn't multiply it; tenant-triggered syncs draw on a per-tenant budget so no community can starve another.

**Wowhead is a link target, not a data source.**

- **Never fetch, scrape, or parse wowhead.com from the server** — not with `HttpClient`, not with a headless browser, not "just once to seed." Fanbyte's terms explicitly bar automated access, and there is no public Wowhead API. A `PreToolUse` hook blocks server-side Wowhead requests, in a file write and in a shell command alike; if you find yourself trying to get around it, you are doing the wrong thing.
- The **only** sanctioned Wowhead integration is client-side: outbound `https://www.wowhead.com/item=<id>` links, decorated by their tooltip script (`https://wow.zamimg.com/js/tooltips.js`) loaded in the Angular app.
- **Everything a crafting or item feature needs is in the Blizzard Game Data API** — item stats, reagents, recipe trees, skill tiers, item sets, journal and encounter data. Reach for those endpoints, not a scraper.
- Item, spell and recipe **IDs come from the Blizzard API**. We reuse them in Wowhead URLs on the widely-held assumption that the ID spaces match — verified by a test, not by faith.

**WarcraftLogs** has a real, documented GraphQL API with OAuth. It is in bounds, under its own rate limiting (points-based, exposed via `rateLimitData`), and it is the only sanctioned source of parse and combat-log data. Never scrape their site.

**AI restrictions**

- **The model never writes SQL, and never receives a connection.** Natural-language queries — over characters, rosters, or the calendar — are answered by having the model emit a *constrained object* validated against a whitelist of fields and operators, which server code translates into LINQ or into calendar commands. A model that can emit SQL is an injection vector with extra steps.
- **Semantic Kernel plugins call facades, not the `DbContext`**, and **never take a model-supplied tenant id**. Grounding data comes through the same layered stack everything else uses, so tenancy, authorization and caching apply to the AI path automatically.
- **Never put another tenant's or another user's private data in a prompt.** Grounding context is scoped to what the caller is already authorized to read, in the resolved tenant.
- **Treat all externally-sourced text as untrusted input** when it reaches a prompt — character names, guild names, guild notes, event descriptions, recruitment applications. All of it is written by people outside the app.
- **Judgement-shaped output must show its work.** Attendance insight, recruitment fit and loot guidance are opinions about people. Each cites the data it used and states what it does not know.

**General restrictions**

- Don't hardcode connection strings, API keys, or `localhost:port` — wire through the AppHost and service discovery / Aspire-injected config. **One sanctioned exception:** `.mcp.json`'s `appdb` server names `http://localhost:8765/sse`. MCP client config is read before the AppHost runs and has no service discovery to read from, so the address has to be literal — which is why the AppHost *pins* that port (`WithEndpoint("http", e => e.Port = 8765)`). The pin and the literal are two halves of one decision.
- External credentials — Blizzard client id/secret, WarcraftLogs client id/secret — are **parameters** (`builder.AddParameter(..., secret: true)`), never literals. Per-tenant Discord webhook URLs are **tenant data**, stored encrypted, never in config.
- Don't put business logic in the AppHost; it stays declarative.
- Don't run `ng serve` by hand. **In development** Aspire launches the Angular dev server via `AddJavaScriptApp` for hot reload; **in production** `AegisScribe.Web` serves the built bundle. Both sit on the same site as the gateway, so the cookie behaves identically in either mode.
- Don't hand-edit generated EF migrations except to review them — with one exception: the vector index and the `PREVIEW_FEATURES` toggle need raw SQL, added deliberately and reviewed.
- Don't commit `bin/`, `obj/`, `node_modules/`, or any secrets.

## Usage

- The world is **local**: Aspire's AppHost orchestrates the API, the sync worker, the Angular app, and all backing resources (SQL Server, Redis, Foundry Local) as local containers. External APIs need real credentials — every gateway is designed to no-op cleanly when they're absent, so the app runs against seeded data, in seeded tenants, with no accounts at all.
- Services find each other through service discovery / Aspire-injected config. The Aspire dashboard is the front door for logs, traces, and health.
- **The Blizzard gateway is cache-first.** A read checks the local store, and only calls Blizzard when the row is missing or stale. That sequencing is *persistence bookkeeping*, so it lives in the DataLayer, not in Business.
- The Angular app is the primary consumer of the API — keep the contract stable.
- Available tooling in `.claude/`: rules auto-load from `.claude/rules/`; task skills live in `.claude/skills/` (`add-endpoint`, `add-tenant-entity`, `new-component`, `add-aspire-resource`, `add-external-sync`, `add-notification`, `add-ai-capability`, `aegisscribe-design-system`); subagents are available but run **read-only**.
- **The design reference carries rules, not just looks.** It draws the Blizzard attribution, the AI generated-content badge and stop control, the interpreted-filter chips, visible staleness, and the tenant switcher — each discharging an obligation above. Dropping one in implementation is a compliance failure, not a styling choice.

## Behavior

- Plan before any change touching more than one file; wait for approval on non-trivial work.
- Use the matching skill in `.claude/skills/` instead of freelancing.
- Run the relevant tests before calling a task done — including the two-tenant isolation test for anything tenant-scoped.
- Make edits in the main session so I can approve them — subagents stay read-only.
- **Verify moving surface before trusting it.** Aspire's AI integrations, SQL Server vector search, and Semantic Kernel are all shipping fast, and parts of each are preview. Confirm package names and API shapes against https://aspire.dev, https://learn.microsoft.com, https://develop.battle.net and https://www.warcraftlogs.com/api/docs rather than a remembered signature. If a preview API has moved, say so instead of writing code against the old one.
