---
name: add-external-sync
description: >
  Bring a new kind of data across from an external source — the Blizzard Game Data/Profile APIs or the
  WarcraftLogs v2 GraphQL API — into AegisScribe's SQL database, end to end. Use for requests like
  "sync mounts for a character", "import the profession recipe tree", "add guild rosters", "pull PvP
  ratings", "bring in raid parses", or "the item catalog is stale". Produces the gateway method, the
  domain entity with its sync metadata, the migration, the cache-first data-layer read, the
  sync-worker job, and the tests — under the rate limits and Terms of Use obligations that govern
  these integrations.
---

# Sync data from an external source

## First: which source, and is it global?

**Almost all synced data is global reference data — no `TenantId`, no query filter.** A character's
gear and a raid parse are the same fact for every community that rosters that character. Syncing per
tenant would multiply API calls by tenant count and breach the rate limit on the second or third
community. Read `.claude/rules/tenancy.md` if that split isn't already clear.

If what you're syncing is genuinely tenant-specific, you're probably not syncing — you're modelling,
and `add-tenant-entity` is the skill you want.

| Source | Read first | Gateway |
|---|---|---|
| Blizzard Game Data / Profile | `references/blizzard-endpoints.md` + `references/blizzard-terms-and-limits.md` | `IBlizzardGateway` |
| WarcraftLogs v2 | `references/warcraftlogs.md` | `IWarcraftLogsGateway` |

Read the reference **before** writing the gateway method. Guessing a Blizzard endpoint path produces
a 404 that reads like "this character doesn't exist" — the most expensive wrong turn available here.
Guessing at WarcraftLogs costs you rate-limit points for a query that returns nothing.

`Integration/<Source>/` is the only place in the solution that knows the source exists. Everything
below either lives there or consumes it.

**Every folder in the steps below is in `src/AegisScribe.Domain/`** — `Integration/`, `Data/`,
`Managers/`, `Facade/` alike — except the sync job itself, which lives in
`src/AegisScribe.SyncWorker/` and reaches gateways and repositories through its reference to Domain.
The worker never references the API.

## The shape of a sync

```
Gateway (HTTP, rate-limited, returns domain entities)
   ↓
DataLayer (cache-first: local → stale? → fetch → upsert)      ← serves user requests
   ↓
Repository (EF upsert, keyed on the Blizzard source id)
   ↑
SyncWorker (scheduled: find rows aging toward 30 days, refresh)  ← keeps the store compliant
```

Two consumers, one gateway. The DataLayer path is *lazy* — it refreshes what someone asked for. The
worker path is *eager* — it refreshes what nobody asked for but the Terms of Use require. You need
both; neither alone is sufficient.

## Steps

1. **Confirm the endpoint.** Find it in `references/blizzard-endpoints.md`. Note the namespace it needs —
   `static-{region}` for reference data, `dynamic-{region}` for realm status, `profile-{region}` for
   character and **guild** data (guild endpoints live under `/data/wow/` but take the profile
   namespace; this trips everyone once).

2. **Domain entity** → `Managers/Models/Domain/`. Two fields are mandatory on anything derived from
   Blizzard, and a review will reject the entity without them:
   - **`LastSyncedAt` (`DateTimeOffset`)** — the 30-day refresh obligation is enforced against it.
   - **A stable Blizzard source id** — deletion requests are executed against it, and it's the upsert
     key. Use Blizzard's own id where one exists; for characters, the natural key is
     `(realmSlug, name)` normalised to lowercase, since Blizzard's character ids aren't stable across
     renames and transfers.

   Add a unique index on the source id. Model only the fields the app actually renders — a
   faithful mirror of Blizzard's JSON is a maintenance burden, not an asset.

3. **Gateway method** → `Integration/Blizzard/`. Add it to `IBlizzardGateway` and the implementation.
   It returns the **domain entity** (or `null` when Blizzard 404s), not a Blizzard-shaped DTO. The
   response deserialization type is internal to this folder and never leaves it.

   ```csharp
   public async Task<Character?> FetchCharacterAsync(string realm, string name, CancellationToken ct)
   {
       using var lease = await _rateLimiter.AcquireAsync(ct);
       var response = await _http.GetAsync(
           $"/profile/wow/character/{realm}/{name.ToLowerInvariant()}?namespace=profile-{_region}&locale={_locale}", ct);

       if (response.StatusCode is HttpStatusCode.NotFound) return null;
       response.EnsureSuccessStatusCode();

       var payload = await response.Content.ReadFromJsonAsync<CharacterResponse>(ct);
       return payload?.ToEntity();
   }
   ```

   Non-negotiables in that method: the **rate-limiter lease**, the **namespace** parameter, a **404
   returning null rather than throwing** (a character that doesn't exist is a normal answer), and the
   bearer token added by the auth handler — never a `?access_token=` query parameter.

4. **Repository upsert** → `Data/`. Match on the source id, update in place, set `LastSyncedAt`.
   Return the persisted entity. One self-contained data operation; no rules.

5. **Cache-first read in the DataLayer** → `Data/`. The shape is fixed; see step 6 of the
   `add-endpoint` skill. Three behaviours that are easy to skip and are each a test:
   - a **fresh** local row returns without touching the gateway,
   - a **gateway failure** falls back to the stale row rather than failing the request,
   - the **fetch happens outside** any transaction callback (the callback is retryable; an HTTP call
     inside one can fire twice).

6. **Sync-worker job** → `src/AegisScribe.SyncWorker/`. A job that finds rows approaching the refresh
   deadline and re-fetches them, with **bounded concurrency** and a budget:

   ```csharp
   var due = await repository.FindStaleAsync(olderThan: _policy.RefreshAfter, take: _policy.BatchSize, ct);
   await Parallel.ForEachAsync(due, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct },
       async (row, token) => { /* fetch + upsert, one lease each */ });
   ```

   Never `Task.WhenAll` over an unbounded list. A guild roster is hundreds of characters, and the
   fastest way to spend an hour's rate-limit budget in ninety seconds is to fan out over one.

   The job must be **idempotent and resumable** — it will be interrupted, and it re-runs from the
   store, not from in-memory state.

7. **Deletion path.** Add the new table to the erasure routine behind `ICharacterDataDeletionFacade`
   (its Business and DataLayer in `AegisScribe.Domain`; see `.claude/rules/external.md`) — by source id,
   in the same transaction as the rest — and extend the test asserting that every entity with a
   Blizzard source id appears there. The routine enumerates tables explicitly rather than reflecting
   over the model, so this is a real edit, not something that happens for free. Its full shape (the
   two trigger routes, the tombstone that stops re-sync, the cache invalidation) is specified in
   `.claude/rules/external.md` → "The deletion path, concretely". A new synced table the deletion
   path doesn't know about is a compliance gap, not a TODO.

8. **Migration.** `dotnet ef migrations add Add<Entity>`, review, `dotnet ef database update`. Commit
   it. Confirm `has-pending-model-changes` is clean.

9. **Seed data.** Add a small seeded sample for this entity so the app demos with no Blizzard
   credentials at all. Offline development is a first-class case — the gateway no-ops cleanly when
   the client id and secret are absent, and the seed is what makes that useful rather than empty.

10. **Tests.**
    - **Gateway:** mocked `HttpMessageHandler`. Assert the request URL carries the right
      **namespace** and **locale**, that the `Authorization` header is `Bearer` (and there is no
      `access_token` query parameter), that a **404 returns null**, that a **429 backs off** rather
      than throwing straight through, and that the JSON maps to the entity correctly. Use a captured
      real response body as the fixture — hand-written JSON hides the shapes Blizzard actually sends
      (nullable specs, missing guild, empty equipment).
    - **DataLayer:** the fresh / stale / gateway-failure / missing matrix from `add-endpoint`.
    - **Worker:** that it selects only rows past the threshold, respects the concurrency bound, and
      is safe to run twice over the same batch.
    - **Compliance:** that the configured refresh interval is **≤ 30 days** — a unit test on config,
      because this is the obligation most likely to be loosened by someone tuning performance.

## Checklist before done
- [ ] The entity is placed in the **global** zone (no `TenantId`, no query filter) unless there is a
      stated reason it is tenant-specific
- [ ] Background sync runs **once, globally** — not once per tenant
- [ ] Any tenant-triggered variant draws on `ITenantSyncBudget` and returns 429 when exhausted
- [ ] Endpoint path and namespace confirmed against `references/blizzard-endpoints.md`
      (or the query against `references/warcraftlogs.md`)
- [ ] Entity has `LastSyncedAt` and a stable Blizzard source id, with a unique index
- [ ] Gateway method returns a domain entity (or null on 404), takes a rate-limiter lease, sends the
      token as a **bearer header**, and passes `namespace` + `locale`
- [ ] Blizzard response types stay inside `Integration/Blizzard/`
- [ ] Cache-first read in the DataLayer; fetch happens outside any transaction callback
- [ ] Sync worker job is bounded-concurrency, budgeted, idempotent and resumable
- [ ] Deletion path covers the new table, by source id
- [ ] Refresh interval ≤ 30 days, with a test that says so
- [ ] Seed data added so the feature demos without credentials
- [ ] Tests pass, including the 404 / 429 / namespace assertions (`dotnet test`)
- [ ] Migration reviewed and committed; `has-pending-model-changes` is clean
