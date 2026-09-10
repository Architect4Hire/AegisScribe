---
paths:
  - src/AegisScribe.ApiService/Integration/**
  - src/AegisScribe.SyncWorker/**
---
# External source rules — Blizzard, WarcraftLogs, Discord & Wowhead

Four external systems, four different postures:

| System | Posture |
|---|---|
| **Blizzard Game Data + Profile** | A real API, called from the server under a binding contract. Sync is **global**. |
| **WarcraftLogs v2** | A real, documented GraphQL API with OAuth and points-based limits. Parse and attendance data. |
| **Discord** | **Outbound only** — we post to per-tenant webhooks. We never read from Discord. |
| **Wowhead** | A **link target**. Never called from the server, at all, for any reason. |

Everything server-side lives under `Integration/`, one folder per system, and nothing outside those
folders knows the systems exist.

## Blizzard — everything goes through the gateway

`Integration/Blizzard/` is the only place in the solution that knows Blizzard exists. It exposes a
single interface, **`IBlizzardGateway`**, returns **domain entities**, and is consumed only by the
DataLayer and the sync worker. No controller, facade, or business class ever holds one. One
interface, not a family — a Game Data client and a Profile client would split on Blizzard's URL
shapes rather than on anything this app cares about, and every consumer would end up injecting both.

- **Typed `HttpClient`** registered with `AddHttpClient<IBlizzardGateway, BlizzardGateway>()`, with
  the standard resilience handler. The base address is regional (`https://us.api.blizzard.com`) and
  comes from config, not a literal.
- **Client-credentials OAuth**, token from the **regional** endpoint
  `https://{region}.battle.net/oauth/token`. The non-regional `oauth.battle.net` host has a history
  of intermittent 403s; don't use it. Cache the token until shortly before expiry; refresh once,
  under a lock, not once per in-flight request.
- **`Authorization: Bearer <token>` header only.** Passing `?access_token=` was disallowed in 2024
  and will fail. If you see that query parameter anywhere, it's a bug.
- **Every call carries `namespace` and `locale` query parameters.** `static-{region}` for reference
  data, `dynamic-{region}` for realm status, `profile-{region}` for anything character- or
  guild-shaped. Getting the namespace wrong returns a 404 that looks like "character doesn't exist",
  which is the single most confusing failure mode in this integration — assert on it in tests.
- **Rate limiting is not optional.** One shared limiter in front of the gateway, sized under the
  documented caps (36,000/hour contractual; the per-second cap is on our client's dashboard). Never
  fan out unthrottled parallel requests — a guild roster sync is the classic way to burn an hour's
  budget in a minute. Use a bounded-concurrency loop, and back off on 429 with the response's retry
  hint.
- **Persist what you fetch.** The gateway does not cache; the DataLayer persists. Every synced row
  records `LastSyncedAt` and its Blizzard source id.
- **Missing credentials are a no-op, not a crash.** When the client id/secret aren't configured, the
  gateway logs once at startup and reports itself unhealthy-but-degraded; the app still runs against
  seeded data. Do not throw on startup — offline development is a first-class case here.

### The obligations that shape the code

Blizzard's Developer API Terms of Use permit storing this data on conditions. Three of them are
architectural, and CLAUDE.md → Restrictions treats them as binding:

- **Refresh at least every 30 days.** The staleness policy is a compliance control. No TTL in config
  may exceed 30 days, and the sync worker's job is to make sure nothing ages past it.
- **Delete on request.** There is a path that removes every stored row derived from an individual's
  data, executed by Blizzard source id. Don't design a schema or a sync that makes this impossible.
  Its shape is specified below — it is not left as an exercise.
- **Attribute Blizzard, imply nothing.** The UI names Blizzard as the source without suggesting
  endorsement or affiliation, and the app's title and URL contain no Blizzard trademark.

### The deletion path, concretely

Several documents here treat "the deletion path" as a thing new tables must be added to. This is what
it is, so nobody has to invent it twice:

- **Where it lives:** `Auth/DataDeletion/` — a `ICharacterDataDeletionFacade` over the normal layer
  stack, not a script. It is a domain operation with rules and an audit trail, so it goes through
  Facade → Business → DataLayer like everything else.
- **Who triggers it:** two routes, one implementation.
  - `POST /api/v1/platform/characters/{realm}/{name}/erasure` behind the **`PlatformAdmin`** policy —
    this is the one that services a request from the person whose data it is, since we have no way to
    authenticate a character's owner as a Battle.net account (that's out of scope, see CLAUDE.md).
    Requests arrive out of band; a platform admin actions them. It is **tenant-less on purpose**:
    character data is global, so erasure crosses every tenant and cannot sit under a tenant route.
  - The same facade method, called by a signed-in user for a character they have **claimed** — the
    claim is the proof of ownership, and the resource-authorization rule in Business enforces it.
    A claim lives in one tenant, but the erasure it authorises is global.
- **What it does**, in one transaction:
  1. Deletes every row keyed to that character's Blizzard source id, across **every** synced table.
  2. Deletes every tenant's `RosterEntry`, `CharacterClaim`, `EventSignup` and `AttendanceRecord`
     referencing it — this is one of the **two sanctioned uses of `IgnoreQueryFilters()`**, because
     the operation is legitimately cross-tenant. Say so in a comment at the call site.
  3. Writes a **tombstone** (`SyncSuppression`: source id, requested-at, actioned-by).
  4. Invalidates the global character cache entries and the tenant-prefixed roster entries in every
     tenant that listed it.
- **The tombstone is the part that's easy to miss.** A plain `DELETE` is undone by the next sync,
  which will cheerfully re-fetch the character and re-insert everything. The gateway checks
  suppressions before fetching and the sync worker excludes them from its stale-row query; a
  suppressed character returns "not available" from the API, not an empty profile.
- **Adding a synced table means adding it here.** The deletion routine enumerates tables explicitly
  rather than reflecting over the model — reflection would silently succeed on a table with no source
  id and give false assurance. There is a test that every entity carrying a Blizzard source id
  appears in the routine, and it is the thing standing between "we added a table" and a compliance
  gap.

Endpoint path shapes, namespaces and regions are catalogued in
`.claude/skills/add-external-sync/references/blizzard-endpoints.md`; the terms and limits in
`.claude/skills/add-external-sync/references/blizzard-terms-and-limits.md`. Read the reference rather than
guessing a path — they are not always shaped the way you'd expect (guild endpoints live under
`/data/wow/` but take the `profile-{region}` namespace).

## Sync is global; tenant-triggered work has a budget

This is the tenancy consequence and it is the reason the app scales at all.

**Blizzard and WarcraftLogs data is global reference data.** One `Character` row serves every tenant
that rosters that character. Syncing is therefore done **once**, by the worker, outside any tenant —
not once per tenant. Copying it per tenant would multiply API calls by tenant count and breach the
rate limit on the second or third community.

But a tenant *can* trigger work — "re-sync our whole roster" is a button, and a 400-member community
pressing it is 400 calls. So:

- Tenant-triggered syncs draw on a **per-tenant budget** (`ITenantSyncBudget`), not on the shared rate
  limiter directly. One community must never be able to starve another.
- Budget exhaustion returns **429 with a retry hint**. Not a silent queue, not a partial result that
  looks complete.
- The worker's own background refresh runs outside tenant budgets, bounded by the global limiter alone.
- The budget is visible to the tenant's owner. A limit nobody can see reads as a bug.

## WarcraftLogs — a real API, with its own rules

`Integration/WarcraftLogs/` exposes `IWarcraftLogsGateway`, returning **domain entities**, consumed
only by the DataLayer and the sync worker.

- **GraphQL, not REST.** Endpoints: `https://www.warcraftlogs.com/api/v2/client` for public data,
  `/api/v2/user` for user-scoped. Token at `/oauth/token`, authorize at `/oauth/authorize`.
- **Client-credentials OAuth** for the public client — same shape as Blizzard, same "cache the token,
  refresh once under a lock" discipline. Credentials are Aspire parameters.
- **Points-based rate limiting, not request-based.** The API exposes `rateLimitData`
  (`limitPerHour`, `pointsSpentThisHour`, `pointsResetIn`) as a GraphQL field. **Query it and respect
  it** — a complex GraphQL query can cost many points, so counting requests tells you nothing. Track
  spend, not calls.
- **Ask for exactly the fields you need.** GraphQL's whole point is that over-fetching is your choice;
  a greedy query is expensive in points and slow.
- Parses and attendance are **global** (a character's log history is the same for everyone), so they
  live in the global zone with `LastSyncedAt` and a source id like everything else.
- Never scrape warcraftlogs.com. There is a real API; use it.

## Discord — outbound webhooks only

- We **post**; we never read. No bot, no gateway connection, no reading messages, no OAuth into
  someone's Discord account.
- The webhook URL is **tenant data**, not configuration: stored on `DiscordWebhook`, encrypted at
  rest, never in `appsettings.json`, never logged. A webhook URL *is* a credential — anyone holding it
  can post to that channel.
- Delivery is **asynchronous and retried** from the sync worker, never inline in a request. A Discord
  outage must not fail a signup.
- Respect the response's rate-limit headers and back off. Per-webhook, not global.
- Every dispatch is recorded — what was sent, to which tenant's webhook, and whether it succeeded — so
  "did the guild get notified" is answerable.

## Wowhead — client-side only, and that is the whole integration

**There is no Wowhead API.** Fanbyte's terms explicitly prohibit automated access — spiders,
crawlers, scrapers, "data mining tools or the like". So:

- **Never fetch wowhead.com from the server.** Not `HttpClient`, not a headless browser, not a
  one-off seeding script, not "just to check". A `PreToolUse` hook blocks server-side Wowhead
  requests — both written into a file and typed as a shell command, because the one-off `curl` is
  the likeliest way this gets broken. The hook is a backstop for the rule, not a puzzle to solve.
- The sanctioned integration is entirely in the browser: render an outbound anchor
  (`https://www.wowhead.com/item=19019`) and let Wowhead's tooltip script decorate it. See
  `.claude/rules/frontend.md` for the markup.
- **IDs come from Blizzard.** We reuse Blizzard's item/spell/recipe ids in Wowhead URLs on the
  widely-held assumption that the id spaces match. Assumption, not documented fact — there is a test
  that pins a handful of known ids. If it ever fails, the linking strategy is what's wrong.

Anything Wowhead-shaped that the server appears to need — item stats, reagents, recipe trees — comes
from the Blizzard Game Data API instead. It has all of it. Reach for
`/data/wow/profession/{id}/skill-tier/{id}` and `/data/wow/recipe/{id}`, not a scraper.
