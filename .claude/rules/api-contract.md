---
paths:
  - src/AegisScribe.ApiService/Controllers/**
  - src/AegisScribe.Domain/Managers/Models/ViewModels/**
  - src/AegisScribe.Domain/Managers/Models/ServiceModels/**
  - contract/**
---
# API contract rules — versioning and evolution

**This file exists because of one fact: you cannot redeploy a phone.**

A web deploy replaces every client at once, so a breaking API change is a bad afternoon. A mobile
release does not. It goes to a store queue, then to users who update when they feel like it, and some
never do. Six months after shipping v3 there will be v1 clients in the wild making requests. Every
rule below follows from that, and none of them were worth this much care when the browser was the
only client.

## Versioning is mandatory

`Asp.Versioning.Http`, **URL segment**, on every route from the first endpoint:

```
/api/v1/characters/{realm}/{name}
/api/v1/t/{tenantSlug}/roster
```

The version comes before the tenant segment: the API's shape is a platform concern, the tenant is a
data concern, and putting the tenant first would imply a tenant could be on a different version.

URL segment rather than a header or query parameter, because it survives being pasted into a bug
report, a log line, a curl, and a cache key. Header-based versioning is more RESTful and much harder
to debug at 2am, which is the trade this repo does not want to make.

**An unversioned route is a defect**, including a health check people will script against.

The one exception is the OAuth protocol surface — `connect/authorize`, `connect/token`,
`connect/revoke`, `connect/logout` and `/.well-known/openid-configuration`. Those paths are the
OpenID/OAuth contract, advertised by the discovery document and fixed by the specs, so they version
with the protocol rather than with this API; `AuthorizationController` is `[ApiVersionNeutral]` for
that reason. Nothing else qualifies.

## Additive-only within a version

Inside `v1`, these are safe:

- adding an endpoint
- adding an **optional** request field
- adding a response field
- adding a new value to an enum *if* clients are required to tolerate unknown values (they are — see
  below)
- relaxing a validation rule

These are breaking, and they mean a new version:

| Change | Why it breaks an old client |
|---|---|
| Removing or renaming any response field | Deserialization fails, or silently yields a default |
| Removing an endpoint or changing its route | 404 on a screen that used to work |
| Adding a **required** request field | Every existing call becomes a 400 |
| Tightening validation | Input that worked yesterday is rejected today |
| Changing a field's type, including `int` → `string` | Deserialization fails |
| Changing the meaning of a field while keeping its name | The worst one — nothing fails, the app is just wrong |
| Changing a default when the field is omitted | Same: silent behaviour change |
| Changing an error's `type` URI or its HTTP status | Client error branching breaks |

The last two are the ones that get shipped by accident, because nothing throws and no test fails.

**Adding a field is not free either.** Every response field is a promise you have to keep for the life
of the version, so a field added "in case the app needs it" is a liability. Ship what a screen uses.

## Clients must tolerate what they don't know

A rule about clients that belongs here because the API depends on it: deserialization ignores unknown
fields, and an unknown enum value maps to a defined `Unknown` member rather than throwing. This is
what makes "adding a response field" additive rather than breaking, and it must hold in the MAUI
client from its first commit — retrofitting it later means the field you already shipped can't be
added safely.

Enums cross the wire as **strings**, never as integers. An integer enum makes reordering a member a
silent data-corruption bug.

## Deprecation is announced, not discovered

When a version is on the way out, say so in the response, on every call, in a form the client can act
on. `Sunset` is RFC 8594:

```
Deprecation: true
Sunset: Wed, 01 Apr 2026 00:00:00 GMT
Link: <https://api.aegisscribe.com/docs/v2>; rel="successor-version"
```

`Asp.Versioning` emits the `api-supported-versions` and `api-deprecated-versions` headers already;
these are in addition. The mobile app logs a deprecation warning it receives, so the sunset date is
visible in telemetry rather than in someone's memory.

**A version is not removed until its telemetry shows no traffic.** "It's been six months" is not the
test; the request count is.

## Forcing an upgrade, when you must

Sometimes a client version is genuinely unusable — a data-corrupting bug, a security fix. The API can
refuse it:

```
426 Upgrade Required
Content-Type: application/problem+json

{ "type": "https://api.aegisscribe.com/problems/client-upgrade-required",
  "title": "This version of AegisScribe is no longer supported",
  "status": 426,
  "minimumVersion": "1.4.0",
  "storeUrl": "..." }
```

Clients send `X-Client-Version` and `X-Client-Platform` on every request so this is possible at all.
The minimum lives in **configuration**, not in code — the entire point is to change it without a
deploy.

Use this **rarely**. It is a hard stop for someone mid-task with no warning, so it is for
correctness and security, never for tidying up old versions. Deprecation headers do that job.

## Errors: RFC 9457, with a stable machine-readable type

One shape, from a global exception handler, with `application/problem+json`:

```json
{ "type": "https://api.aegisscribe.com/problems/tenant-membership-required",
  "title": "You are not a member of this community",
  "status": 403,
  "detail": "...",
  "instance": "/api/v1/t/emberfall/roster",
  "traceId": "00-4bf92f...-01" }
```

**The `type` URI is the contract; `title` and `detail` are not.** Clients branch on `type`. That means
a `type` may never be repurposed, and it also means every error a client is expected to handle needs
one — a bare 400 with prose tells the app nothing it can act on. Reword `title` freely; it exists for
humans.

`traceId` is on every error. It is what turns a user's screenshot into a log query.

Validation failures use `ValidationProblemDetails` with per-field errors, because a mobile form needs
to know *which* field to highlight, not that something was wrong.

## Pagination is cursor-based

**Never offset.** A roster sorted by rank, paged by offset, on a list that changes while someone
scrolls, duplicates and skips rows — and mobile infinite scroll is exactly that scenario.

```
GET /api/v1/t/emberfall/roster?limit=50&cursor=eyJpZCI6...

{ "items": [ ... ], "nextCursor": "eyJpZCI6...", "hasMore": true }
```

The cursor is opaque to the client — base64 of a stable sort key plus the tie-breaking id. It is
**not** an offset in disguise, and it is not a place to hide a tenant id or anything else a client
could tamper with; anything a cursor decodes to is re-authorized server-side like any other input.

`limit` has a **server-enforced maximum**. An unbounded `limit` is a denial-of-service endpoint with
extra steps.

## Writes are idempotent

Mobile networks fail *after* the server commits. The app cannot tell that from failing before, so it
retries, and a raid signup gets created twice.

Every `POST` that creates something accepts an `Idempotency-Key` header (a client-generated UUID). The
first request stores key → response; a repeat within the retention window (24 hours) replays the
stored response instead of acting again. Same key with a *different* body is a 422, not a silent
overwrite.

Applies to signups, applications, invitations, role changes, notification sends — anything a user
would be upset to see happen twice.

`PUT` and `DELETE` are naturally idempotent; keep them that way. `DELETE` on something already gone is
`204`, not `404` — the client's intent is satisfied.

## Conditional requests

**`ETag` + `If-None-Match` on collection and detail GETs.** A `304` costs a few bytes where a full
roster costs kilobytes, and on a metered connection with a background refresh that difference is the
user's data allowance.

**`If-Match` on updates.** Two officers editing the same roster note is a real scenario, and without
it the second write silently wins. A mismatch is `412`, and the app is expected to show the conflict
rather than retry blindly.

## Sync is delta-based

Collections a mobile app caches accept `?since={ISO-8601 UTC}` and return only what changed, including
**tombstones** for deletions:

```json
{ "items": [...], "deleted": ["a1b2...", "c3d4..."], "syncedAt": "2026-04-01T12:00:00Z" }
```

Without tombstones, a deleted event never disappears from the phone. The client sends back the
`syncedAt` it was given, never a clock reading of its own — device clocks are wrong often enough to
matter, and a fast device clock silently skips changes forever.

Every syncable entity therefore needs a server-set `UpdatedAt` and soft-delete, which is a schema
decision — see `.claude/skills/add-tenant-entity/SKILL.md`.

## Payload discipline

- **Timestamps** are ISO 8601 with an explicit offset, always UTC on the wire. Never a local time,
  never a bare date for something that is an instant. The client renders in the device's zone; the
  API has no opinion about the user's zone except where recurrence needs one (see
  `.claude/skills/add-tenant-entity/references/time-and-recurrence.md`).
- **Response compression** on. Rosters and item catalogues compress extremely well.
- **No chatty screens.** A screen needing seven round-trips is a slow screen on mobile latency; give
  it one composed endpoint. That is what the facade layer is for.
- **Images are URLs, never base64 in JSON.** Blizzard's media endpoints give URLs; pass them through
  so the platform's image cache can do its job.

## The contract is a committed artefact

The generated OpenAPI document is committed at `contract/openapi.v1.json` and regenerated by a test.
A change to it appears in the diff of the pull request that caused it, which is the only reliable way
a breaking change gets noticed by a human before it gets noticed by a user.

`@api-contract-checker` reviews that diff against the table above. A red diff is not automatically
wrong — it is a question that must be answered in the PR description.
