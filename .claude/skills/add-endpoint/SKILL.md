---
name: add-endpoint
description: >
  Add a new REST endpoint to the AegisScribe ASP.NET Core API using the layered
  controller → facade → business → data layer → repository/gateway architecture. Use whenever creating
  or extending API routes — e.g. "add an endpoint to search characters by realm", "add a POST route to
  claim a character", "expose a guild's roster". Produces the controller action, view models, service
  models, facade (authorize + validate + cache), business (translate + apply rules + map), data layer
  (compose data operations, including cache-first Blizzard reads), repository, validators, mappers, DI
  wiring, and tests that match this repo's conventions.
---

# Add an API endpoint

**Controllers only — never minimal API route mapping** (`app.MapGet`/`MapPost`/`MapMethods`), and
that includes Identity glue and OpenIddict's own protocol endpoints, not just feature routes. See
`.claude/rules/backend.md` for why: minimal API's body-inference can misjudge a service parameter as
a request body and crash the host at startup, a failure mode controllers don't have because DI is
unambiguous through the constructor.

**First, is this endpoint tenant-scoped?** If it reads or writes anything a community owns — rosters,
ranks, events, signups, notifications, applications — the answer is yes, and
`.claude/rules/tenancy.md` plus the `add-tenant-entity` skill govern it. Tenant-scoped endpoints:

- live under `/api/v1/t/{tenantSlug}/...`,
- carry a tenant membership policy (`TenantMember` / `TenantOfficer` / `TenantOwner`),
- cache under **tenant-prefixed keys**,
- and owe a **two-tenant isolation test**.

Endpoints over global reference data — characters, items, recipes — are tenant-less, cache under bare
keys, and need none of that. Getting this backwards in either direction is the most expensive mistake
available in this repo.

**Second: this endpoint is a public contract with a client you cannot redeploy.** The mobile app is
shipped to devices and updates on its users' schedule, so every endpoint here owes the obligations in
`.claude/rules/api-contract.md` from its first commit — retrofitting them means breaking the app in
the field. Concretely, before you write the controller:

- the route is **versioned** — `/api/v1/...`, and unversioned is not an option;
- a collection is **cursor**-paginated with a server-enforced max `limit`, never offset;
- a create accepts **`Idempotency-Key`** — mobile networks fail after the server commits, and the app
  retries;
- errors are `problem+json` with a **stable `type` URI** the client can branch on;
- a collection the app caches supports **`?since=`** and returns tombstones;
- response fields are ones a screen actually uses. Every field is a promise for the life of the
  version.

Work in `src/AegisScribe.ApiService/`, organized **type-first**. The orchestration layers —
`Controllers/`, `Facade/`, `Business/` — and the data-access layer, `Data/`, sit at the project root,
alongside `Integration/` (external sources), `Auth/` (Identity wiring) and `Ai/`. The rest of what
they lean on lives under the `Managers/` umbrella: validators, models, mappers, and infrastructure.
Stop for review before running migrations.

## Target layout

```
AegisScribe.ApiService/
├── Controllers/            # <Feature>Controller.cs — HTTP surface (ViewModel in, ServiceModel out)
├── Facade/                 # I<Feature>Facade  + <Feature>Facade   (validate VM + cache + return SM)
├── Business/               # I<Feature>Business + <Feature>Business (VM→domain, domain rules, domain→SM)
├── Data/                   # AegisScribeDbContext
│                           #   I<Feature>DataLayer  + <Feature>DataLayer  (compose data operations)
│                           #   I<Feature>Repository + <Feature>Repository (EF queries)
├── Integration/Blizzard/   # IBlizzardGateway — the only door to the Blizzard API
├── Auth/                   # Identity endpoints, policies, role seeding, the data-deletion path
├── Ai/
│   ├── Plugins/            # [KernelFunction] classes — each wraps a facade
│   ├── Prompts/            # system prompts, one file per capability
│   └── Filters/            # the constrained-filter schema + LINQ translator
├── Managers/
│   ├── Validators/         # FluentValidation validators for the view models
│   ├── Models/
│   │   ├── ViewModels/     # inbound request types — the ONLY thing the controller binds
│   │   ├── ServiceModels/  # outbound response types — the ONLY thing the API returns
│   │   ├── Domain/         # EF entities + domain exceptions
│   │   └── Identity/       # ApplicationUser
│   ├── Mappers/            # VM→domain, domain→ServiceModel (extension methods)
│   └── Infrastructure/     # cross-cutting (e.g. the global exception handler, rate limiter)
├── Migrations/
└── Program.cs
```

## The three model types (this is the core idea)

A request enters as a **ViewModel** and a response leaves as a **ServiceModel** — those are the only
types on the wire. In between, work is done on **Domain** entities. There is no separate DTO layer:
the domain entity *is* the internal shape, so a loaded entity maps directly to a service model.
Nothing leaks — no EF entity ever reaches the controller, and no view model ever reaches the DB.

| Type | Folder | Lives between | Who creates it |
|------|--------|---------------|----------------|
| **ViewModel** | `Managers/Models/ViewModels/` | client → controller → facade | model binder |
| **Domain** entity | `Managers/Models/Domain/` | business ↔ data ↔ EF ↔ gateway | business (from the VM) / EF (on load) / the gateway (from Blizzard JSON) |
| **ServiceModel** | `Managers/Models/ServiceModels/` | business → facade → controller → client | business (from the entity) |

The gateway returning **domain entities** rather than its own Blizzard-shaped DTOs is deliberate: it
means the DataLayer can treat "read from SQL" and "read from Blizzard" as two sources of the same
thing, which is what makes the cache-first composition in step 6 a three-line method instead of a
mapping exercise.

## The layers (strict responsibilities)

```
Controller  →  Facade              →  Business                →  DataLayer          →  Repository
  (HTTP:        (validate the VM +      (translate VM→domain,      (compose data          (EF queries)
   VM in,        cache; return SM)       apply domain rules,        operations; owns    ↘  Gateway
   SM out,                               domain→SM)                 cache-first +          (Blizzard
   [Authorize])                                                     transactions)           HTTP)
```

- **Controller** (`Controllers/`) — HTTP only: bind the **ViewModel**, call the facade, return an
  `ActionResult<ServiceModel>`. No validation, cache, logic, or data access; never sees an entity.
  `[Authorize(Policy = "...")]` **is** allowed here — declarative HTTP metadata, not logic. A bare
  `[Authorize(Roles = "...")]` is not; policies are defined once in `Auth/`.
- **Facade** (`Facade/`) — the boundary: **validates** the ViewModel (via a `Managers/Validators/`
  validator), handles **caching** of ServiceModels (read-through on queries, invalidate on writes),
  and returns ServiceModels. No orchestration, mapping, or EF. Depends on `I<Feature>Business`.
- **Business** (`Business/`) — **domain rules and translation**: translates the validated
  **ViewModel → Domain** entity, applies data-dependent domain rules (e.g. "a character may only be
  claimed by one account", "you may only edit a character you've claimed"), and maps the returned
  **Domain entity → ServiceModel**. Resource authorization lives here — it needs to read something
  first, which is exactly what makes it a rule rather than a policy. No validation, caching, or EF.
  Depends on `I<Feature>DataLayer`.
- **DataLayer** (`Data/`) — **composes data operations**: turns one logical read or write into
  however many repository *and gateway* calls it takes, so business asks once and does no sequencing.
  Two jobs are its alone:
  - **Cache-first external reads** — check the local store, and call Blizzard only when the row is
    missing or stale, then persist what comes back. Staleness is *persistence bookkeeping*, not a
    domain rule; see "where a rule goes" below.
  - **The transaction boundary** for anything it composes: it knows which calls form one operation,
    so it is the only layer that can say where atomicity starts and ends.

  Passes an operation straight through when a single repository call already *is* the whole
  operation. Depends on `I<Feature>Repository` and (where relevant) `IBlizzardGateway` — it holds no
  `DbContext`, so every query still belongs to the repository. No rules, mapping, cache, or validation.
- **Repository** (`Data/`) — **data only**: EF Core queries against the Aspire-provided `DbContext`,
  plus `ExecuteInTransactionAsync` (the one thing it exposes that isn't a query — it takes the data
  layer's whole operation as a callback and runs it in a transaction, so EF stops here).
  Detail reads and writes return the **Domain entity**; a **list** read projects straight to its
  summary **ServiceModel** in SQL (counts without materializing child rows — the one place data
  touches an outbound model, to keep the projection). Each method is one self-contained data
  operation. No rules, cache, or validation; may translate a DB constraint violation into the domain
  exception.
- **Gateway** (`Integration/Blizzard/`) — **outbound HTTP only**: authenticated, rate-limited calls
  to the Blizzard API, returning domain entities. No EF, no cache, no rules. Only the DataLayer and
  the sync worker consume it. Full rules in `.claude/rules/external.md`.

Each layer depends on the **interface** of the one below it (`ICharacterFacade` → `ICharacterBusiness`
→ `ICharacterDataLayer` → `ICharacterRepository` / `IBlizzardGateway`), never on a concrete class or a
lower layer's dependencies.

**Where a rule goes when it's ambiguous.** The split between business and data layer is by *reason*,
not by call count.

The bright-line test: **delete the extra call, and ask what breaks.**

- If the user now gets a **wrong answer or an action that should have been refused** — it was a
  domain rule. **Business.**
- If the answer is still correct, but the **store is left stale, orphaned, or inconsistent** — it was
  bookkeeping. **DataLayer.**

Worked through, on the three that come up most here:

| Extra call | Delete it and… | Layer |
|---|---|---|
| Read the existing claim before inserting a new one | a character gets two owners — a refusal that should have happened didn't | Business |
| Check `LastSyncedAt`, refetch from Blizzard if stale | the answer is still a correct answer, just an older one | DataLayer |
| Reap categories/tags orphaned by a delete | the user's delete succeeded and was correct; rows are left behind | DataLayer |
| Verify the caller owns the character before updating it | someone edits a character that isn't theirs | Business |

When the test genuinely comes out ambiguous, it belongs in **Business** — a rule misfiled as
bookkeeping silently loses an authorization or a validity check, while bookkeeping misfiled as a rule
is only untidy.

## Steps

1. **ViewModel** → `Managers/Models/ViewModels/`. Define the inbound request type(s) (e.g.
   `SearchCharactersViewModel`, `ClaimCharacterViewModel`). The only shape the controller binds.

2. **ServiceModel** → `Managers/Models/ServiceModels/`. Define the outbound response type(s) (e.g.
   `CharacterSummaryServiceModel`, `CharacterDetailServiceModel`). The only shape the API returns.

3. **Validator** → `Managers/Validators/`. Add a FluentValidation `AbstractValidator<TViewModel>` for
   each write ViewModel. Shape/format rules only — realm slug format, name length, page size caps.
   Data-dependent rules that need the DB go in business.

4. **Mappers** → `Managers/Mappers/`. Add the two seams you touch: `ViewModel.ToEntity()` (business,
   VM→domain) and `Entity.ToServiceModel()` (business, domain→SM).

5. **Repository (`ICharacterRepository` / `CharacterRepository`)** → `Data/`. Add the
   query/persistence method. Detail reads and writes return the **Domain entity** (with the needed
   `Include`s); a list read projects to its summary **ServiceModel** in SQL. Writes accept a Domain
   entity. May translate a unique-index violation into the domain exception. Keep each method a
   single self-contained data operation. No rules, cache, or validation.

6. **DataLayer (`ICharacterDataLayer` / `CharacterDataLayer`)** → `Data/`. Add the method business
   calls, composing however many repository (and gateway) calls the operation takes into one. When a
   single repository call already is the whole operation, the method is a one-line pass-through —
   that is expected, and it is still the method business depends on, so the seam holds when the
   operation later grows a second call.

   **Cache-first reads of Blizzard-backed data belong here**, and they all have the same shape:

   ```csharp
   public async Task<Character?> GetCharacterAsync(string realm, string name, CancellationToken ct)
   {
       var local = await _repository.FindCharacterAsync(realm, name, ct);
       if (local is not null && !_staleness.IsStale(local.LastSyncedAt))
           return local;

       var fresh = await _gateway.FetchCharacterAsync(realm, name, ct);   // may return null
       if (fresh is null)
           return local;                                                  // stale beats nothing

       return await _repository.UpsertCharacterAsync(fresh, ct);
   }
   ```

   Three things that shape is deliberately doing: it **falls back to the stale row** when Blizzard is
   unreachable rather than failing the request; it **persists** what it fetched, because the ToU
   permits storage and the rate limit punishes re-fetching; and the staleness threshold comes from
   the injected policy, which is capped at 30 days by the Terms of Use — see
   `.claude/rules/external.md`.

   **If the composition writes more than once, make it atomic** — hand the whole operation to the
   repository as a callback:
   ```csharp
   // Fetch FIRST — outside the callback. The callback is retryable; an HTTP call inside it fires
   // again on every retry, spending rate limit and possibly acting twice.
   var fresh = await _gateway.FetchGuildRosterAsync(realm, guild, ct);

   var result = await _repository.ExecuteInTransactionAsync(
       async token =>
       {
           // ... ONLY repository calls in here ...
           await _repository.UpsertGuildAsync(fresh.Guild, token);
           await _repository.ReplaceMembersAsync(fresh.Guild.Id, fresh.Members, token);
           return fresh.Guild;   // a throw on any leg rolls the whole thing back
       },
       ct);
   ```
   Note what is *not* in that callback: the gateway call. "Hand the whole operation to the
   repository" means the whole **persistence** operation. Anything that isn't a repository call —
   an HTTP request, a model call, a queue publish — is staged before the callback or performed
   after it.
   **A callback, not a `BeginTransactionAsync` that hands back a transaction — and that shape is
   forced, not chosen.** Aspire's SQL Server integration enables retry-on-failure, and EF Core's
   execution strategy refuses to run inside a transaction the caller opened itself ("does not support
   user-initiated transactions"): it can't replay a unit whose boundaries it doesn't own. Passing the
   whole unit in is what lets the two coexist.

   Two consequences worth internalising: the operation **may run more than once**, so it must be safe
   to repeat; and only work done *through this repository* is rolled back — so **the gateway call
   goes outside the callback**. Fetch first, then transact. An HTTP call inside a retryable
   transaction is the bug this rule exists to prevent.

   Depends only on `ICharacterRepository` (+ `IBlizzardGateway` where the feature is Blizzard-backed);
   no `DbContext` of its own.

7. **Business (`ICharacterBusiness` / `CharacterBusiness`)** → `Business/`. Add the method the facade
   calls. Detail reads: map the returned **entity → ServiceModel**. List reads: pass the data layer's
   projected summaries through. Writes: translate the **ViewModel → Domain** entity, apply
   data-dependent domain rules (throwing the domain exception on violation), call the data layer, and
   map the persisted **entity → ServiceModel**. **Resource authorization is a rule and lives here** —
   "this claim belongs to another user" throws a domain exception the global handler maps to 403.
   Depends only on `ICharacterDataLayer`.

8. **Facade (`ICharacterFacade` / `CharacterFacade`)** → `Facade/`. Add the method the controller
   calls. It **validates** the ViewModel with the injected `IValidator<TViewModel>` (the global
   handler maps `ValidationException` → 400), applies **caching** of ServiceModels (read-through on
   queries; invalidate the affected keys on writes), and returns the ServiceModel. Depends on
   `ICharacterBusiness`, the validator, and the cache abstraction. No mapping, orchestration, or EF.

   Note there are two caches in play and they are not the same thing:

   **Key the cache by zone.** A tenant-scoped ServiceModel caches under `t:{tenantId}:...`; a global
   one caches under a bare key. Getting this backwards either leaks one community's data to another
   or fragments the shared character cache N ways. There is no third option and no judgement call.

   | | Redis ServiceModel cache | The SQL store, as a Blizzard cache |
   |---|---|---|
   | Owned by | the **Facade** | the **DataLayer** (staleness check) |
   | Holds | ServiceModels | domain entities |
   | TTL | **minutes** — 5 for character detail, 15 for guild rosters, 60 for static reference data | **days**, capped at 30 by the Terms of Use |
   | Purpose | page-load latency | rate limit + compliance |

   Don't conflate them. In particular, "the DataLayer must not cache" in the checklist means this
   Redis cache — the staleness check against the local store *is* the DataLayer's job. And don't try
   to solve Blizzard staleness here; a five-minute Redis entry over a three-week-old row is still a
   three-week-old answer.

9. **Controller** → `Controllers/<Feature>Controller.cs`. Add a thin action that binds the ViewModel,
   calls the facade, and returns `ActionResult<ServiceModel>`.

   Route and policy follow the zone:
   ```csharp
   [Route("api/v{version:apiVersion}/t/{tenantSlug}/roster")]          // tenant-scoped
   [Authorize(Policy = "TenantMember")]           // or TenantOfficer / TenantOwner

   [Route("api/v{version:apiVersion}/characters")]                      // global reference — no tenant, no policy
   ```
   Anonymous character lookup is the app's front door and stays anonymous. **Never bind `tenantSlug`
   into a ViewModel and pass it down** — the tenant is resolved by middleware into `ITenantContext`;
   an action that hands a tenant identifier to a lower layer has re-opened the hole the middleware
   closed.

10. **DI wiring.** Register the layers in `Program.cs` (scoped), and register validators:
    ```csharp
    builder.Services.AddScoped<ICharacterRepository, CharacterRepository>();
    builder.Services.AddScoped<ICharacterDataLayer, CharacterDataLayer>();
    builder.Services.AddScoped<ICharacterBusiness, CharacterBusiness>();
    builder.Services.AddScoped<ICharacterFacade, CharacterFacade>();
    ```
    Validators need no registration of their own: `Program.cs` already calls
    `AddValidatorsFromAssemblyContaining<Program>()`, which picks up every validator in the assembly.
    Don't add a second `AddValidatorsFromAssemblyContaining` line per feature.

11. **Cache backing.** Use the Aspire Redis client integration for the distributed cache (keyed to
    the AppHost `cache` resource) — no hardcoded connection details. Read-through + invalidate lives
    only in the facade, and it caches ServiceModels.

12. **Tests (per layer, mock the layer below).**
    - **Repository:** integration test against a real/containerized SQL Server — the query returns the
      expected entities / summary service models.
    - **DataLayer:** unit test with a mocked `ICharacterRepository` and `IBlizzardGateway` — that a
      **fresh** local row short-circuits without touching the gateway, a **stale** one fetches and
      upserts, a **gateway failure** falls back to the stale row rather than throwing, and a **missing**
      row with a missing remote returns null. For a composed write: that it calls the right repository
      methods **in the right order**, commits last, and does **not** commit when a leg throws. A mocked
      transaction only proves a commit was *asked for*, so back any atomic composition with one
      **real-database** test that a mid-composition failure leaves the store untouched.
    - **Business:** unit test with a mocked `ICharacterDataLayer` — entity→ServiceModel mapping and the
      list pass-through on reads, plus the VM→domain translation, the domain rule, and the resource
      authorization failure on writes.
    - **Facade:** unit test with a mocked `ICharacterBusiness`, a real validator, and an in-memory cache
      — cover a cache **hit**, a cache **miss**, and a **validation failure**.
    - **Endpoint:** integration test (`WebApplicationFactory`) for the happy path plus one validation
      failure — asserting on ServiceModels, posting ViewModels. For a protected route, add
      **401 unauthenticated** and **403 insufficient rank**. For a **tenant-scoped** route, also add
      the three from `.claude/rules/tenancy.md`: tenant B cannot see tenant A's rows, an unknown
      tenant returns **404 not 403**, and a write cannot set another tenant's `TenantId`.
      Run `dotnet test`.

13. **Migration (only if the model changed).** `dotnet ef migrations add <Name>`, review, then
    `dotnet ef database update`. Commit the migration, and confirm
    `dotnet ef migrations has-pending-model-changes` is clean. (If you move a namespace that appears
    in the migration snapshot — a Domain entity or the DbContext — update those strings too.)

## Armory domain notes

Core entities: `Character` (realm, name, level, class, spec, item level, faction, `LastSyncedAt`),
`CharacterEquipment` / `EquippedItem`, `Item` (Blizzard item id, quality, slot, item level,
`Embedding`), `Profession` / `Recipe` / `ReagentSlot`, `Guild` / `GuildMember`, `Realm`,
`CharacterClaim`. Common routes: search characters, get a character with equipment and specs, get a
guild roster, list a character's professions and craftable recipes, claim a character, admin role
management.

**Every Blizzard-derived entity carries `LastSyncedAt` and a Blizzard source id.** Not optional —
the first drives the 30-day refresh obligation, the second makes deletion requests executable. A new
synced entity without both is incomplete.

## Checklist before done
- [ ] Route is **versioned** (`/api/v{n}/...`); collections cursor-paginated with a max `limit`;
      creates accept `Idempotency-Key`; errors are `problem+json` with a stable `type`
- [ ] `contract/openapi.v1.json` regenerated and reviewed in the same change
- [ ] Files live in the type-first folders above — controller/facade/business/data at the project
      root; validators, models, mappers, infrastructure under `Managers/`
- [ ] Only ViewModels enter and only ServiceModels leave the API — no EF entity crosses the
      controller boundary
- [ ] Zone decided: tenant-scoped route + membership policy + tenant-prefixed cache key, **or**
      global route with none of those
- [ ] `tenantSlug` never bound into a ViewModel or passed to a lower layer
- [ ] **Two-tenant isolation test** for any tenant-scoped route, asserted through the endpoint
- [ ] Controller does HTTP only — no validation, cache, logic, or data access; authorization is a
      **policy** attribute, not a role string
- [ ] Facade owns validation + caching; no orchestration, mapping, or EF
- [ ] Business translates VM→domain, applies domain rules **and resource authorization**, maps
      domain→ServiceModel; no validation, cache, EF, or multi-call data sequencing
- [ ] DataLayer composes repository/gateway calls into whole data operations (pass-throughs where one
      call suffices) and owns the cache-first staleness check; no rules, mapping, validation,
      `DbContext`, or **ServiceModel/Redis** caching (the staleness check against the local store is
      not that cache — see step 8)
- [ ] Any DataLayer composition that writes more than once is wrapped in a transaction, commits only
      on success, and makes **no HTTP call inside the callback**
- [ ] Repository returns domain entities (list projects to its summary ServiceModel) and does queries
      only, one self-contained data operation per method; no rules, cache, or validation
- [ ] Blizzard is reached only through `IBlizzardGateway`, and only from the DataLayer or sync worker
- [ ] Any new Blizzard-derived entity has `LastSyncedAt` and a Blizzard source id
- [ ] Each layer depends on the interface below it (`IFacade`→`IBusiness`→`IDataLayer`→`IRepository`)
- [ ] `DbContext` and cache obtained via the Aspire integrations (no hardcoded connection strings)
- [ ] Validation returns the shared error shape on failure
- [ ] Tests per layer pass, incl. the facade cache-hit / cache-miss / validation-failure trio, the
      data layer's fresh / stale / gateway-failure / missing matrix, the call-order and rollback
      assertions for any composed write, and 401 + 403 for any protected route (`dotnet test`)
- [ ] Migration reviewed and committed, and `has-pending-model-changes` is clean (if the model changed)
