# WarcraftLogs API v2 — auth, GraphQL, and the points budget

> Reference for the `add-external-sync` skill. Verified against community client documentation;
> **the official docs at https://www.warcraftlogs.com/api/docs block automated fetching (403)**, so
> open them in a browser to confirm anything not covered here. The interactive schema explorer there
> is the authority on field names.

## What it's for

The only sanctioned source of **parse, ranking and log-derived attendance** data. Everything gear-,
item- and roster-shaped comes from Blizzard; WarcraftLogs answers "how did this character actually
perform, and did they show up".

It is a genuinely different API from Blizzard's — GraphQL, points-based limits, a different OAuth
client — so it gets its own gateway (`IWarcraftLogsGateway`) in `AegisScribe.Domain/Integration/WarcraftLogs/`, never a
method bolted onto the Blizzard one.

## Endpoints

| Purpose | URL |
|---|---|
| Public data (client credentials) | `https://www.warcraftlogs.com/api/v2/client` |
| User-scoped private data | `https://www.warcraftlogs.com/api/v2/user` |
| OAuth authorize | `https://www.warcraftlogs.com/oauth/authorize` |
| OAuth token | `https://www.warcraftlogs.com/oauth/token` |

**We use `/api/v2/client` only.** Public data covers parses, rankings and report participation, which
is everything this app needs. The `/user` endpoint requires an authorization-code flow with each
user's WarcraftLogs account — that's a consent story we haven't designed, so it is **out of bounds**
alongside Battle.net OAuth (CLAUDE.md → Scope).

## Auth

Three flows exist — client credentials, authorization code, and PKCE. **Use client credentials.**

```
POST https://www.warcraftlogs.com/oauth/token
Authorization: Basic base64(client_id:client_secret)
Content-Type: application/x-www-form-urlencoded

grant_type=client_credentials
```

Same discipline as the Blizzard gateway: cache the token until shortly before expiry, refresh once
under a lock, send it as `Authorization: Bearer <token>`. Credentials are Aspire parameters
(`warcraftlogs-client-id`, `warcraftlogs-client-secret`), never literals — the `secret-guard` hook
blocks them anyway.

Missing credentials **degrade, don't crash**: log once at startup, report unhealthy-but-degraded, and
let the app run on seeded data. Same rule as Blizzard.

## Rate limiting is points-based — count spend, not calls

This is the part that differs most from Blizzard and the part most likely to be got wrong.

A single GraphQL query can cost many points depending on how much it asks for, so **counting requests
tells you nothing**. The API exposes its own limit state as a queryable field:

```graphql
query { rateLimitData { limitPerHour pointsSpentThisHour pointsResetIn } }
```

Design consequences:

- The gateway tracks **points**, not requests. `ITokenBucket`-style limiters keyed on request count are
  the wrong abstraction here.
- Poll `rateLimitData` periodically — cheaply, and on a schedule, not before every call — and hold the
  last known spend. When `pointsSpentThisHour` approaches `limitPerHour`, stop issuing queries and
  wait out `pointsResetIn`.
- **Ask for exactly the fields you need.** Over-fetching in GraphQL is your own choice and you pay for
  it in points. A query that pulls a whole report when you wanted three fighters' parses is a
  self-inflicted outage.
- Back off on throttling responses rather than retrying immediately.
- Expose the current spend in the dashboard. A budget you can't see is a budget you'll blow.

## GraphQL shape

One POST, a query document, and variables:

```csharp
var query = """
    query CharacterParses($name: String!, $server: String!, $region: String!) {
      characterData {
        character(name: $name, serverSlug: $server, serverRegion: $region) {
          id
          zoneRankings
        }
      }
      rateLimitData { pointsSpentThisHour limitPerHour }
    }
    """;
```

Notes that save time:

- **Query documents live in files** under `Integration/WarcraftLogs/Queries/`, not in string literals
  scattered through the gateway — same reasoning as prompts living in files. They're reviewable and
  diffable that way.
- Ask for `rateLimitData` **alongside** the real query when it's free to do so; you get budget state
  without a second call.
- GraphQL returns **200 with an `errors` array** for query-level failures. A gateway that only checks
  the HTTP status will treat a failed query as success and deserialize nulls. Check `errors` first,
  always.
- A character with no logs is a **normal answer**, not an error — return null, exactly like a Blizzard
  404.

## It is not an image source

**Don't come here for icons, portraits or crests.** WarcraftLogs is a combat-log analysis API; its
domain is reports, fights, rankings and parses. Nothing in it is a hosted image you would render in
this app. The only image-ish field anyone documents is `avatar` on the `User` type, and we don't touch
`/api/v2/user` at all.

Two cautions if you go looking anyway:

- The schema exposes game reference types (abilities, items) that **may carry an `icon` field**. I have
  not been able to verify this — every page under `warcraftlogs.com/v2-api-docs/` returns 403 to
  automated fetching, so check the interactive schema explorer in a browser before relying on one.
- **If such a field exists, it is a filename, not a URL**, and the convention in this ecosystem is to
  resolve those filenames against `wow.zamimg.com` — which is **Wowhead's CDN**. Do not do that. Using
  Wowhead as an image host for our own UI is exactly the server-side dependency `.claude/rules/external.md`
  rules out, and it would put a third party's bandwidth on the critical path of every page.

Every image in this app comes from Blizzard's media endpoints. They are catalogued in
`references/blizzard-endpoints.md` → "Images and media".

## Zone and modelling

Parse data is **global reference data**: a character's log history is the same fact for every
community. So `CharacterParse` and friends carry **no `TenantId`**, live in the global zone, and
follow the usual rules — `LastSyncedAt` plus a stable source id.

What's tenant-scoped is the *use* of it: a community's attendance policy, its raid roster, its own
`AttendanceRecord` rows. Those are `add-tenant-entity` territory.

Model only what you'll render. The WarcraftLogs schema is large and most of it is for a different kind
of application.

## Testing

- Mock `HttpMessageHandler`, fixtures from **captured real responses** — including one with a
  populated `errors` array and one for a character with no logs.
- Assert the gateway sends a **bearer header**, posts to `/api/v2/client`, and **fails on a non-empty
  `errors` array** even when the status is 200.
- Assert points tracking: that the gateway stops issuing queries once the known spend approaches the
  limit, rather than discovering it by being throttled.
- Never call the real API from a test.
