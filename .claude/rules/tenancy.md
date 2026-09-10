---
paths:
  - src/AegisScribe.ApiService/**
  - src/AegisScribe.SyncWorker/**
  - src/AegisScribe.MigrationService/**
  - src/AegisScribe.Tests/**
  - src/web/**
---
# Tenancy rules — the invariant everything else sits on

AegisScribe is multi-tenant at the core. A **tenant is a Community**: a group that owns one or more
guilds, possibly across realms and factions. Isolation is a **shared database with a `TenantId`
discriminator and a global EF Core query filter**.

This is the rule with the worst failure mode in the repo. A missed filter doesn't crash, doesn't log,
and doesn't fail a test that only ever uses one tenant — it silently shows Community A the roster of
Community B. Everything below exists to make that hard to do by accident, and there is a dedicated
subagent, `tenant-isolation-auditor`, whose only job is to look for it.

## The two zones — get this right first

**Not everything is tenant-scoped, and the half that isn't is the important half.**

| Zone | Carries `TenantId`? | Query filter? | Entities |
|---|---|---|---|
| **Global reference** | **No** | **No** | `Realm`, `Character`, `CharacterEquipment`, `EquippedItem`, `Item`, `Profession`, `Recipe`, `ReagentSlot`, `Guild`, `GuildMember`, `SyncSuppression` |
| **Tenant-scoped** | **Yes** | **Yes** | `Tenant`, `TenantMembership`, `RosterEntry`, `TenantRank`, `CalendarEvent`, `EventSignup`, `AttendanceRecord`, `Notification`, `NotificationPreference`, `DiscordWebhook`, `DeviceRegistration`, `CharacterClaim`, `AuditLog`, `RecruitmentApplication` |

Character gear, items and recipes are **public Blizzard data**. They are not anybody's private
information and they are identical for every tenant. Copying them per tenant would multiply Blizzard
API calls by tenant count — the fastest possible way to burn the rate limit and breach the Terms of
Use — and would mean re-embedding the same item catalogue N times.

So the model is: **one global `Character` row, N tenant `RosterEntry` rows pointing at it.** The
character is the fact; the roster entry is *this community's* relationship to it — rank, notes,
main/alt link, join date.

The same split applies to guild ranks, and the two are easy to confuse:

- **`GuildMember.BlizzardRank`** — global, 0–9, straight from the Blizzard roster endpoint. What the
  game says.
- **`RosterEntry.TenantRankId` → `TenantRank`** — tenant-scoped, user-defined ("Raider", "Trial",
  "Social", "Officer"). What the community says.

They are not the same thing and neither derives from the other. A community may run two raid teams
inside one in-game guild; a community may span two guilds. Model both.

## Tenant resolution

Path-based, resolved once in middleware, never read from a request body:

```
/api/v1/t/{tenantSlug}/roster
/api/v1/t/{tenantSlug}/calendar/events
/api/auth/...            ← tenant-less: sign-in, registration
/api/platform/...        ← tenant-less: platform admin
/api/characters/...      ← tenant-less: global reference reads
```

Middleware resolves `{tenantSlug}` to a `Tenant`, verifies the caller has a `TenantMembership` in it,
and populates a scoped `ITenantContext`. The `DbContext` takes `ITenantContext` in its constructor.

- **Never accept `TenantId` from the client** — not in a body, not in a header, not in a query
  parameter. It comes from the route and is validated against membership. A client-supplied tenant id
  is a horizontal privilege escalation with a friendly name.
- **A request with no resolved tenant may not touch a tenant-scoped entity.** `ITenantContext` throws
  rather than returning a default when a tenant-scoped query runs outside tenant scope.
- Path-based, not subdomain-based, because subdomains need wildcard DNS and wildcard TLS for no
  benefit the app actually uses.

## The query filter, and the one prohibition

```csharp
modelBuilder.Entity<CalendarEvent>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);
```

Applied to **every** tenant-scoped entity, in `OnModelCreating`, without exception.

**`IgnoreQueryFilters()` is banned in application code.** There are exactly two sanctioned uses, both
outside the request path and both requiring a comment saying which one it is:

1. The **migration service** and **sync worker**, where operations are legitimately cross-tenant.
2. The **erasure routine**, which must reach every tenant's rows for a given Blizzard source id.

Anywhere else — a controller, facade, business class, or repository serving a request — it is a
finding, no matter how convenient. If you need cross-tenant data in a request, the answer is a
platform-admin endpoint with its own explicit authorization, not a disabled filter.

## Writes: the interceptor sets `TenantId`, not you

```csharp
public override int SaveChanges() { StampTenant(); return base.SaveChanges(); }
```

A `SaveChanges` interceptor stamps `TenantId` on every added tenant-scoped entity from
`ITenantContext`, and **throws** if an entity is being added with a `TenantId` that doesn't match the
ambient tenant. Business code never assigns `TenantId` by hand — if you find yourself typing
`entity.TenantId = ...`, the ambient context is missing and that is the bug.

## Cache keys

The Redis ServiceModel cache is shared, so a key without a tenant serves one community's data to
another.

- **Tenant-scoped ServiceModels:** the key **must** start with the tenant —
  `t:{tenantId}:roster:{page}`. No exceptions.
- **Global reference ServiceModels:** the key **must not** include a tenant —
  `char:{realm}:{name}`. Including one would fragment the cache N ways and lose the whole benefit of
  a shared character store.

Getting this backwards in either direction is a finding. When in doubt, ask which zone the entity is
in; the answer decides the key.

## Authorization is membership, not Identity roles

ASP.NET Core Identity roles are **platform-level only** (`PlatformAdmin`). They cannot express
"officer in Community A, member in Community B", which is the normal case.

Tenant authorization is a domain concept:

```csharp
public class TenantMembership { public string UserId; public Guid TenantId; public TenantRole Role; }
public enum TenantRole { Member, Officer, Owner }
```

Policies are tenant-aware requirements that read the resolved tenant and the caller's membership in
it:

```csharp
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("TenantMember",  p => p.AddRequirements(new TenantRoleRequirement(TenantRole.Member)))
    .AddPolicy("TenantOfficer", p => p.AddRequirements(new TenantRoleRequirement(TenantRole.Officer)))
    .AddPolicy("TenantOwner",   p => p.AddRequirements(new TenantRoleRequirement(TenantRole.Owner)))
    .AddPolicy("PlatformAdmin", p => p.RequireRole("PlatformAdmin"));
```

`Owner` implies `Officer` implies `Member`; the requirement handler encodes that once so no endpoint
lists three roles. Resource rules that depend on data — "this roster entry belongs to another
member" — still live in **Business**, as they always did.

## External data and the per-tenant budget

Blizzard sync is **global**, because the data is global. That is what stops tenant count from
multiplying API calls. But *tenant-triggered* work still needs a ceiling:

- A tenant hitting "re-sync the whole roster" must draw from a **per-tenant budget**, not the shared
  rate limiter directly. One community must not be able to starve every other community's syncs.
- Budget exhaustion is a **429 with a retry hint**, not a silent queue.
- The sync worker's own background refresh runs outside tenant budgets and is bounded by the global
  limiter alone.

The same applies to model calls: AI features draw on a per-tenant quota, and the quota is visible to
the tenant's owner.

## AI grounding is tenant-scoped by construction

Semantic Kernel plugins call facades, and facades run inside the resolved tenant — which is exactly
why that rule exists. Two additions here:

- A plugin must never take a tenant id as a **model-supplied parameter**. The tenant comes from the
  ambient context; a model that can name a tenant can name someone else's.
- **Vector search over tenant-scoped content** (event descriptions, officer notes, applications) must
  filter on `TenantId` *inside the SQL query*, not by post-filtering results. Post-filtering leaks
  through result counts and ordering even when the rows themselves are dropped.
- Global reference vectors (the item catalogue) are shared and carry no tenant.

## Erasure and export

- **Blizzard erasure** (Terms of Use) operates on **global** character data by source id, crosses all
  tenants, and is one of the two sanctioned `IgnoreQueryFilters` uses. It also removes the
  `RosterEntry` rows in every tenant that referenced the character.
- **Tenant deletion** removes every tenant-scoped row for that `TenantId` and touches no global data.
- **Tenant export** produces that tenant's rows only. Both are enumerated explicitly, never by
  reflection.

## Testing: the two-tenant test is the whole game

A single-tenant test suite proves nothing about isolation — every query passes when there is only one
tenant's data to return.

**Every tenant-scoped feature needs a test that seeds two tenants and asserts tenant B cannot see
tenant A's rows** — through the endpoint, not the repository. Add it at the same time as the feature,
not later. Also assert:

- A request for a tenant the caller has no membership in returns **404, not 403** — a 403 confirms the
  tenant exists, which is itself a leak.
- A tenant-scoped write from tenant A cannot set a row's `TenantId` to B.
- A cache entry populated by tenant A is not returned to tenant B.

## Checklist before done
- [ ] Every new entity is deliberately placed in the **global** or **tenant-scoped** zone, and the
      placement is defensible from the two-zone table above
- [ ] Tenant-scoped entities carry `TenantId` **and** a global query filter; global entities carry
      neither
- [ ] `TenantId` never arrives from the client; it is resolved from the route and checked against
      membership
- [ ] No `IgnoreQueryFilters()` outside the migration service, sync worker, or erasure routine — and
      those carry a comment saying which
- [ ] `TenantId` is stamped by the `SaveChanges` interceptor, never assigned in business code
- [ ] Tenant-scoped cache keys are prefixed with the tenant; global cache keys are not
- [ ] Authorization uses tenant membership policies, not Identity roles (except `PlatformAdmin`)
- [ ] Tenant-triggered external work draws on a per-tenant budget
- [ ] AI plugins take no model-supplied tenant id; tenant vector search filters in SQL
- [ ] A **two-tenant isolation test** exists for this feature and passes
- [ ] Unknown-tenant access returns 404, not 403
