---
name: add-tenant-entity
description: >
  Add a tenant-scoped entity to AegisScribe — roster entries, ranks, calendar events, signups,
  attendance, notifications, applications, audit rows, or anything else a community owns privately.
  Use for requests like "add guild ranks", "build the raid calendar", "track attendance", "let
  officers leave notes on a member", "add recruitment applications". Covers the zone decision, the
  TenantId + query filter mechanics, the SaveChanges interceptor, tenant cache keys, membership
  policies, and the two-tenant isolation test that every one of these owes.
---

# Add a tenant-scoped entity

Read `.claude/rules/tenancy.md` first. This skill is the procedure; that file is the law.

## Step 0 — decide the zone, and be able to defend it

Before anything else, answer one question: **is this a fact about the world, or a fact about this
community?**

- *"Thornwake wears a 639 chestpiece"* — a fact about the world. **Global.** No `TenantId`.
- *"Thornwake is a Raider in Ashes of Dawn and signed up for Tuesday"* — a fact about this community.
  **Tenant-scoped.** `TenantId` plus a query filter.

The test that catches most mistakes: **would two communities disagree about it?** If they could
disagree, it's tenant-scoped. If disagreeing would just mean one of them is wrong, it's global.

Getting this wrong in the tenant-scoped direction is expensive but safe — you duplicate data and
multiply API calls. Getting it wrong in the **global** direction is a **data leak**: you've put one
community's private note in a table everyone reads.

If the answer is "global", stop — you want `add-external-sync` or plain `add-endpoint`, not this.

## Steps

Every folder below is in `src/AegisScribe.Domain/` except controllers and authorization policies,
which live in `src/AegisScribe.ApiService/` — see the two-project layout in the `add-endpoint` skill.

1. **Entity** → `Managers/Models/Domain/`. It implements `ITenantScoped`:
   ```csharp
   public interface ITenantScoped { Guid TenantId { get; set; } }

   public class RosterEntry : ITenantScoped
   {
       public Guid   Id { get; set; }
       public Guid   TenantId { get; set; }          // stamped by the interceptor, never by you
       public int    CharacterId { get; set; }        // FK into the GLOBAL Character table
       public Guid?  TenantRankId { get; set; }
       public int?   MainRosterEntryId { get; set; }  // alt linking — a community's own judgement
       public string? OfficerNote { get; set; }
       public DateTimeOffset JoinedAt { get; set; }
   }
   ```
   The marker interface is what lets the interceptor and the model builder find these generically.
   Every index that matters starts with `TenantId` — a filtered query that can't use an index is a
   table scan per tenant.

2. **Query filter** → `OnModelCreating`. Applied to every `ITenantScoped` entity, ideally by
   convention so nobody can forget one:
   ```csharp
   foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                .Where(t => typeof(ITenantScoped).IsAssignableFrom(t.ClrType)))
   {
       modelBuilder.Entity(entityType.ClrType).AddQueryFilter<ITenantScoped>(
           e => e.TenantId == _tenantContext.TenantId);
   }
   ```
   Convention over per-entity registration is the point: a developer adding entity number forty
   shouldn't have to remember step 2 at all.

3. **Interceptor** → the `SaveChanges` override stamps `TenantId` from `ITenantContext` on every added
   `ITenantScoped` entity, and **throws** if one arrives already carrying a different tenant. If you
   catch yourself writing `entity.TenantId = ...` in business code, the ambient context is missing and
   that is the actual bug.

4. **Foreign keys point the right way.** A tenant-scoped entity may reference a global one
   (`RosterEntry.CharacterId → Character.Id`). A global entity **never** references a tenant-scoped
   one — that would make global data depend on one community's rows and break every other tenant when
   it's deleted.

5. **Migration.** `dotnet ef migrations add <Name>`, review, apply. Check the generated SQL actually
   contains your `TenantId` column and indexes — a filter over a column with no index is a scan.

6. **Repository / DataLayer / Business / Facade / Controller** → follow `add-endpoint` as normal. Two
   differences:
   - The route is `/api/v1/t/{tenantSlug}/...` and carries a tenant policy (`TenantMember`,
     `TenantOfficer`, `TenantOwner`).
   - Cache keys are **prefixed with the tenant**: `t:{tenantId}:roster:{page}`. Never a bare key.

7. **Authorization.** Rank-shaped checks are policies. Data-shaped checks ("this note is another
   member's") are **domain rules in Business**, throwing the domain exception the handler maps to 403.
   Don't blur them.

8. **Audit anything an officer does to someone else.** Role changes, removals, rank edits, note edits,
   event deletions — an `AuditLog` row with who, what, which tenant, when, and before/after. Once
   officers can act on other people's data, "who did this" stops being optional.

9. **Tests — and the two-tenant one is not negotiable.**
   - **Isolation:** seed **two** tenants with similar data. Assert through the **endpoint** that tenant
     B cannot read, update or delete tenant A's rows. A single-tenant suite proves nothing: every
     query passes when there's only one tenant's data to return.
   - **404 not 403** for a tenant the caller has no membership in. A 403 confirms the tenant exists.
   - **Cross-tenant write:** tenant A cannot create a row with tenant B's `TenantId`.
   - **Cache:** an entry populated by tenant A is not served to tenant B.
   - Plus the usual per-layer tests from `add-endpoint`.

## Time, recurrence and signups

Calendar work has its own sharp edges — timezones, recurrence expansion, the signup state machine,
and what happens when an event moves. They're in `references/time-and-recurrence.md`. Read it before
building anything with a date on it; getting recurrence wrong is the kind of bug that quietly
double-books a guild for a month.

## Checklist before done
- [ ] Zone decision made deliberately and defensible by the "would two communities disagree" test
- [ ] Entity implements `ITenantScoped`; every meaningful index starts with `TenantId`
- [ ] Query filter applied **by convention**, not per-entity registration
- [ ] `TenantId` stamped by the interceptor; no assignment in business code
- [ ] No global entity holds a foreign key to a tenant-scoped one
- [ ] Route is `/api/v1/t/{tenantSlug}/...` with a tenant membership policy
- [ ] Cache keys prefixed with the tenant
- [ ] Officer actions on other people's data write an `AuditLog` row
- [ ] **Two-tenant isolation test** exists and passes, through the endpoint
- [ ] Unknown-tenant access returns 404, not 403
- [ ] Cross-tenant write rejected; cross-tenant cache read impossible
- [ ] `dotnet test` green, migration reviewed and committed
