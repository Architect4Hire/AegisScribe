# 🛡️ AegisScribe

A **multi-tenant World of Warcraft armory and guild-operations platform**, built with
**Aspire + ASP.NET Core + Angular + .NET MAUI + SQL Server**, developed hand-in-hand with **Claude Code**.

A community signs up, links its guilds, and gets: every member's characters with gear and progression,
a roster with its own ranks, a raid calendar with signups and attendance, notifications where the
guild actually lives, and an AI layer that reads all of it.

Everything runs locally: Aspire's AppHost orchestrates the API, the sync worker, the Angular app, and
all backing resources (SQL Server 2025, Redis, Foundry Local) as local containers. External APIs need
credentials, and the app runs on seeded data without any of them.

This repo is two things at once: a genuinely useful product, and a public, reproducible demonstration
of driving a real .NET stack agentically with Claude Code.

## About the name

*Aegis* is the shield of Athena — the thing that carries a device and protects the bearer. *Scribe* is
the one who keeps the record. Between them they name what this is: a register of what people bear.

It also borrows no Blizzard mark, which is not incidental — Blizzard's Developer API Terms of Use
forbid an application whose title or URL contains one. If you fork this, keep that constraint: no
*Warcraft*, *WoW*, *Azeroth*, *Battle.net*, *Blizzard*, or expansion names in the project name or the
domain.

This project is not affiliated with or endorsed by Blizzard Entertainment. Game data is retrieved from
the Blizzard Developer API.

## Stack

- **Orchestration:** Aspire 13 (AppHost + ServiceDefaults) on .NET 10
- **Backend:** ASP.NET Core Web API · EF Core 10 (SQL Server 2025)
- **Tenancy:** community-as-tenant, shared database, `TenantId` discriminator + global query filter
- **Edge:** YARP 2.3 BFF gateway. Three public hostnames on one registrable domain — `app.*` (SPA), `bff.*` (gateway), `api.*` (API — the mobile app's door, and ops)
- **Auth:** **OpenIddict** on the API — authorization code + PKCE, rotating refresh tokens. Cookie for the browser, tokens for mobile; Identity for identity, **tenant membership** for authorization
- **Mobile:** .NET MAUI, an OAuth public client calling the API directly
- **API contract:** versioned from the first endpoint, because a shipped phone can't be redeployed
- **AI:** Microsoft.Extensions.AI over Azure AI Foundry, with Semantic Kernel for plugins
- **Vector search:** SQL Server 2025 native `VECTOR` columns via EF Core 10 — same database
- **Frontend:** Angular (standalone components, strict TypeScript, `scribe-` prefix)
- **External:** Blizzard Game Data + Profile APIs · WarcraftLogs v2 · Discord webhooks (outbound) ·
  mobile push · Wowhead client-side tooltips only

## What it does

**Armory** — look up any character by realm and name. Gear with item-quality colours, specs, item
level, professions, raid and keystone progression, guild.

**Roster** — a community's own view of its people: its own rank ladder, alt linking, officer notes,
sortable, sitting alongside the in-game guild ranks without pretending they're the same thing.

**Calendar** — raid events with recurrence, a signup state machine, signup windows, and attendance
tracked separately from signups because the two disagree constantly and the disagreement is the point.

**Notifications** — an in-app centre as the system of record, plus per-tenant Discord webhooks and
mobile push, with retry, per-channel delivery status, and per-community preferences.

**AI** — a crafting and gear advisor grounded in real data; semantic item search over an embedded
catalogue; natural-language roster queries; natural-language scheduling with preview-and-confirm;
composition gap analysis; attendance insight; recruitment fit and loot guidance.

## The edge — two front doors, one resource server

Three publicly addressable deployables, three hostnames, one registrable domain. `AegisScribe.Web`
serves the Angular bundle at `app.*`; `AegisScribe.Gateway` is a YARP **backend-for-frontend** at
`bff.*`; the API is at `api.*`, where the **mobile app** and ops both reach it directly.

**OpenIddict on the API issues every token**, and both front ends run the same authorization code +
PKCE flow. The difference is what happens to the tokens afterwards:

- **The browser** never touches them. The gateway is a *confidential* client: it holds the tokens in
  a Redis session and hands the browser an `HttpOnly` cookie. An XSS in the SPA cannot exfiltrate a
  portable credential, and logout is instant because the session is server-side.
- **The mobile app** is a *public* client. It has no gateway, no cookie and no origin, so it holds
  tokens in platform secure storage, refreshes them itself, and signs in through the system browser —
  never an embedded WebView.

One protocol, two client types, and an API with a single authorization path to reason about. That the
mobile app holds tokens is **not** an argument for the SPA doing the same; they have different threat
models, which is the whole reason the gateway exists.

The public API hostname pays for itself with its own rules: no CORS policy at all (a native app
doesn't need one, and its absence still stops any browser reading a response cross-origin), per-client
rate limiting, and mandatory versioning.

`app.*` and `bff.*` are cross-origin but **same-site**, which is what keeps the cookie viable —
genuinely different registrable domains would force `SameSite=None`, and Safari blocks those by
default. The trade is that CORS applies, and the cookie's `Domain` attribute means no untrusted
content may ever be hosted on a subdomain.

Rules: [`.claude/rules/gateway.md`](.claude/rules/gateway.md).

## Multi-tenancy, in one paragraph

A **tenant is a community** — a group owning one or more guilds, possibly across realms and factions.
Isolation is a shared database with a `TenantId` discriminator and a global EF query filter. The
important half is what *isn't* tenant-scoped: character gear, items and recipes are **public Blizzard
data**, identical for everyone, so they live in a **global zone** with no `TenantId` at all. One
global `Character` row, N tenant `RosterEntry` rows pointing at it. Copying that data per tenant would
multiply Blizzard API calls by tenant count and breach the rate limit on the third community.

Because a user can be an officer in one community and a member in another, **Identity roles are not
the authorization mechanism** — tenant membership is. Identity has exactly one role, `PlatformAdmin`.

The full rules are in [`.claude/rules/tenancy.md`](.claude/rules/tenancy.md), and there is a dedicated
read-only subagent, `@tenant-isolation-auditor`, whose only job is to find leaks. That exists because
the failure mode is silent: a missing query filter doesn't throw, doesn't log, and passes every test
written against a single tenant.

## Repo layout

```
.
├── CLAUDE.md          # project constitution — auto-loaded by Claude Code every session
├── .claude/           # the toolkit: rules, skills, subagents, hooks
├── .mcp.json          # MCP servers: playwright, aspire, and the local appdb server
├── .gitignore         # build output, and everything credential-shaped
├── .gitattributes     # keeps the shell hooks LF — a CRLF shebang breaks them
├── design/
│   └── aegisscribe-armory.html   # the design system + screen reference (open it in a browser)
├── docs/
│   ├── architecture.md           # high-level architecture and the key design decisions
│   └── scrub-prompts.md          # the seam-by-seam build sequence, and reusable templates
├── src/
│   ├── AegisScribe.AppHost/           # Aspire orchestrator
│   ├── AegisScribe.ServiceDefaults/   # telemetry, health, resilience, discovery
│   ├── AegisScribe.Gateway/           # YARP BFF — public at bff.*, owns the session
│   ├── AegisScribe.Web/               # serves the Angular bundle — public at app.*
│   ├── AegisScribe.ApiService/        # API + EF Core + Identity + OpenIddict + tenancy + AI — public at api.*
│   ├── AegisScribe.Mobile/            # .NET MAUI client — OAuth public client, not orchestrated
│   ├── AegisScribe.SyncWorker/        # external sync, embedding backfill, notification dispatch
│   ├── AegisScribe.MigrationService/  # applies migrations once, before the API starts
│   ├── AegisScribe.Tests/
│   └── web/                           # Angular app
└── README.md
```

## The design system

[`design/aegisscribe-armory.html`](design/aegisscribe-armory.html) is the visual source of truth — a
standalone page, no build step. It defines the tokens, shows every shared component, and draws six
screens with their loading, empty, degraded and error states. Tokens are copied from it, never
invented; a literal hex under `src/web/src/app/` is a defect.

Its organising idea is that **three colour systems never mix.** The chrome is near-monochrome gunmetal
with a single brass accent, because the game's item-quality palette already owns green, blue, purple
and orange — and on an armory screen, quality colour is what players read before any text. Semantic
state stays on pills and tints and never borrows an item's border.

The reference also draws the obligations — Blizzard attribution, the AI generated-content badge and
stop control, interpreted-filter chips, visible staleness, the tenant switcher, masked webhook URLs —
so they can be copied rather than remembered.

> **New here?** [`docs/architecture.md`](docs/architecture.md) is the full picture — context and
> container views, the tenancy model, the request path, and the seventeen decisions that are
> load-bearing along with what each one costs.

## Architecture in one paragraph

Requests go `Controller → Facade → Business → DataLayer → Repository | Gateway`. ViewModels in,
ServiceModels out, domain entities between, and no EF entity crosses the API boundary. The facade
validates and caches; business owns domain rules and resource authorization; the data layer composes
whole data operations and owns both the transaction boundary and the cache-first staleness check
against external APIs; the repository does EF and gateways do HTTP. Semantic Kernel plugins call
**facades**, which is how the AI path inherits tenancy, authorization and caching instead of
reimplementing them. Full reasoning in
[`.claude/skills/add-endpoint/SKILL.md`](.claude/skills/add-endpoint/SKILL.md).

## How this repo gets built

`src/` is built by driving Claude Code through the **SCRUB microprompts** in
[`docs/scrub-prompts.md`](docs/scrub-prompts.md) — 108 small prompts across 17 phases, one seam each.
Each names the rules it must honour, the skills to use, and the subagents that review it, so the
toolkit does the remembering and the prompt only says what to build.

Part 2 of that file is the reusable half — feature slices, migrations, refactors, debugging, new
external data, new tenant-scoped features, and the pre-release audit. After scaffolding, that's what
you actually use.

## Getting started

1. **Prerequisites:** .NET 10 SDK, the Aspire CLI, Node.js, and a container runtime (Docker/Podman).
2. **Make the hooks executable:**
   ```bash
   chmod +x .claude/hooks/*.sh
   git update-index --chmod=+x .claude/hooks/*.sh   # record the bit IN git
   ```
   The second line matters on Windows, where Git usually has `core.fileMode=false`
   and won't notice the first. Without it every clone gets non-executable hooks, and
   `secret-guard` and `wowhead-guard` stop guarding silently. `.gitattributes` keeps
   these files LF for the same reason — a CRLF shebang fails with "bad interpreter".
3. **Credentials (all optional):**
   ```bash
   # Blizzard — https://develop.battle.net
   dotnet user-secrets set "Parameters:blizzard-client-id"     "<id>"     --project src/AegisScribe.AppHost
   dotnet user-secrets set "Parameters:blizzard-client-secret" "<secret>" --project src/AegisScribe.AppHost
   # WarcraftLogs — https://www.warcraftlogs.com/api/clients
   dotnet user-secrets set "Parameters:warcraftlogs-client-id"     "<id>"     --project src/AegisScribe.AppHost
   dotnet user-secrets set "Parameters:warcraftlogs-client-secret" "<secret>" --project src/AegisScribe.AppHost
   ```
   Skip them and the app still runs — every gateway no-ops and you get seeded data in two seeded
   tenants.
4. **Build it.** Open [`docs/scrub-prompts.md`](docs/scrub-prompts.md) and run Part 1 from 0.1.
5. **Run it:** `aspire run`

> **SQL Server must be a 2025 image.** The default 2022 tag has no `VECTOR` type, and every AI feature
> depends on one. The AppHost pins it; don't unpin it.

## External data, and what's off limits

**Blizzard** data is stored under conditions that are architectural rather than legal boilerplate:
refresh at least every 30 days, delete an individual's data on request, attribute without implying
endorsement, no trademark in title or URL, stay under the rate limits. Enforced in code, tested, and
audited by `@external-compliance`.

**WarcraftLogs** has a real documented GraphQL API with OAuth and **points-based** rate limiting —
counting requests tells you nothing, so the gateway tracks points via `rateLimitData`.

**Discord** is outbound only. We post to per-tenant webhooks; we never read. A webhook URL is a
credential: encrypted at rest, masked in responses, absent from logs.

**Wowhead is a link target, not a data source.** There is no public Wowhead API and Fanbyte's terms
prohibit automated access, so the server never calls them — the integration is a client-side tooltip
script decorating outbound links. A `PreToolUse` hook blocks server-side Wowhead requests from being
written at all. Everything a crafting feature needs is in Blizzard's profession and recipe endpoints.

Details and quoted terms:
[`.claude/skills/add-external-sync/references/blizzard-terms-and-limits.md`](.claude/skills/add-external-sync/references/blizzard-terms-and-limits.md).

## Verify before trusting

Aspire's AI integrations, SQL Server vector search and the .NET AI stack all ship fast, and parts of
each are preview. See "Verify before trusting" in [`.claude/README.md`](.claude/README.md) for what's
GA, what isn't, and which package names have recently moved.

## License
MIT.
