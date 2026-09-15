using AegisScribe.Domain.Context;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AegisScribe.Domain.Data;

public class AegisScribeDbContext(DbContextOptions<AegisScribeDbContext> options, ITenantContext tenantContext)
    : IdentityDbContext<ApplicationUser, IdentityRole, string>(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantMembership> TenantMemberships => Set<TenantMembership>();
    public DbSet<TenantSyncBudgetWindow> TenantSyncBudgetWindows => Set<TenantSyncBudgetWindow>();
    public DbSet<TenantGuild> TenantGuilds => Set<TenantGuild>();
    public DbSet<TenantRank> TenantRanks => Set<TenantRank>();
    public DbSet<RosterEntry> RosterEntries => Set<RosterEntry>();
    public DbSet<CharacterClaim> CharacterClaims => Set<CharacterClaim>();
    public DbSet<GuildRankName> GuildRankNames => Set<GuildRankName>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Realm> Realms => Set<Realm>();
    public DbSet<Character> Characters => Set<Character>();
    public DbSet<CharacterEquipment> CharacterEquipments => Set<CharacterEquipment>();
    public DbSet<EquippedItem> EquippedItems => Set<EquippedItem>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<Profession> Professions => Set<Profession>();
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<ReagentSlot> ReagentSlots => Set<ReagentSlot>();
    public DbSet<Guild> Guilds => Set<Guild>();
    public DbSet<GuildMember> GuildMembers => Set<GuildMember>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.Property(t => t.Slug).HasMaxLength(64).IsRequired();
            entity.Property(t => t.Name).HasMaxLength(200).IsRequired();
            entity.Property(t => t.TimeZoneId).HasMaxLength(64).IsRequired();

            // Route resolution (/api/v1/t/{slug}/...) looks tenants up by slug, not id.
            entity.HasIndex(t => t.Slug).IsUnique();
        });

        modelBuilder.Entity<TenantMembership>(entity =>
        {
            // A user has exactly one membership per tenant — the natural key is the PK.
            entity.HasKey(m => new { m.TenantId, m.UserId });

            entity.HasOne<Tenant>()
                .WithMany()
                .HasForeignKey(m => m.TenantId)
                // Tenant deletion is an explicit, application-orchestrated routine
                // (tenancy.md), not a DB cascade.
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(m => m.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Serves "list my tenants" (GET /api/v1/me), which isn't filtered by tenant first.
            entity.HasIndex(m => m.UserId);
        });

        // Tenant-scoped (tenancy.md): it implements ITenantScoped, so the convention loop at the bottom
        // of this method gives it the global query filter automatically and the interceptor stamps
        // TenantId on insert.
        modelBuilder.Entity<TenantSyncBudgetWindow>(entity =>
        {
            entity.HasOne<Tenant>()
                .WithMany()
                .HasForeignKey(w => w.TenantId)
                .OnDelete(DeleteBehavior.Restrict);

            // One window row per community, and the index is load-bearing rather than tidy: the
            // atomic consume in SyncBudgetRepository relies on a duplicate insert being REJECTED when
            // two first-ever requests race, instead of quietly producing two windows that would each
            // grant a full budget.
            entity.HasIndex(w => w.TenantId).IsUnique();
        });

        modelBuilder.Entity<TenantGuild>(entity =>
        {
            entity.HasOne<Tenant>()
                .WithMany()
                .HasForeignKey(g => g.TenantId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(g => g.Guild)
                .WithMany()
                .HasForeignKey(g => g.GuildId)
                .OnDelete(DeleteBehavior.Restrict);

            // A community links a given guild once. Index starts with TenantId, like every
            // tenant-scoped index (tenancy.md) — every query against this table filters on it first.
            entity.HasIndex(g => new { g.TenantId, g.GuildId }).IsUnique();
        });

        // Tenant-scoped (tenancy.md), and the query filter is again NOT registered here — the
        // convention loop at the bottom of this method applies it because TenantRank implements
        // ITenantScoped. That is the whole point of the loop: adding an entity should not require
        // remembering to wire up isolation.
        modelBuilder.Entity<TenantRank>(entity =>
        {
            entity.Property(r => r.Name).HasMaxLength(32).IsRequired();

            // Exactly "#rrggbb". The width is part of the guard that keeps a tenant-configured colour
            // from becoming CSS injected into the rank pill's --rank-color (see TenantRank.Colour);
            // RankValidationRules enforces the shape at the edge, this enforces the size at rest.
            entity.Property(r => r.Colour).HasMaxLength(7).IsRequired();

            entity.HasOne<Tenant>()
                .WithMany()
                .HasForeignKey(r => r.TenantId)
                // Tenant deletion is an explicit, application-orchestrated routine
                // (tenancy.md), not a DB cascade.
                .OnDelete(DeleteBehavior.Restrict);

            // A community names a rank once. Starts with TenantId like every tenant-scoped index —
            // every query against this table filters on it first — and is load-bearing rather than
            // tidy: TenantRankBusiness pre-checks the name, but this index is what decides the race
            // when two officers add "Raider" at the same moment.
            //
            // Case-insensitive by the column's default collation, which is deliberate: "Raider" and
            // "raider" are the same rank to the people reading the roster.
            entity.HasIndex(r => new { r.TenantId, r.Name }).IsUnique();
        });

        // Tenant-scoped (tenancy.md), and the row where the two zones meet: it holds an FK INTO the
        // global Character table. That direction only — a global entity holding an FK back to a
        // tenant-scoped one would make shared reference data depend on one community's rows.
        modelBuilder.Entity<RosterEntry>(entity =>
        {
            entity.Property(e => e.OfficerNote).HasMaxLength(1000);

            entity.HasOne<Tenant>()
                .WithMany()
                .HasForeignKey(e => e.TenantId)
                // Tenant deletion is an explicit, application-orchestrated routine
                // (tenancy.md), not a DB cascade.
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Character)
                .WithMany()
                .HasForeignKey(e => e.CharacterId)
                // Erasure is likewise explicit (tenancy.md): the routine removes the RosterEntry rows
                // in every tenant that referenced a character, rather than letting the database decide
                // what a deleted character does to N communities' rosters.
                .OnDelete(DeleteBehavior.Restrict);

            // Alt linking (7.3), self-referencing and one level deep. Restrict rather than a cascade,
            // and that has a consequence worth knowing before 7.4: removing a roster entry that other
            // entries call their main will FAIL until those alts are detached. That is the right
            // default — silently orphaning or deleting somebody's other characters is worse — but the
            // remove path has to handle it deliberately.
            entity.HasOne(e => e.MainRosterEntry)
                .WithMany()
                .HasForeignKey(e => e.MainRosterEntryId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.TenantRank)
                .WithMany()
                .HasForeignKey(e => e.TenantRankId)
                // Restrict, not SetNull, and that is the load-bearing half of 7.2's rank-delete rule:
                // clearing a rank off every holder is the silent kind of damage. TenantRankBusiness
                // counts holders and refuses with a 409 first, so this constraint is the backstop for
                // a race rather than the thing officers normally meet.
                .OnDelete(DeleteBehavior.Restrict);

            // A character sits on a given community's roster once. Starts with TenantId like every
            // tenant-scoped index — every query against this table filters on it first.
            entity.HasIndex(e => new { e.TenantId, e.CharacterId }).IsUnique();

            // What the rank-in-use count reads. Without it, refusing a rank delete scans the whole
            // community's roster, and that check now runs on every rank deletion.
            entity.HasIndex(e => new { e.TenantId, e.TenantRankId });

            // "Does anything call this entry its main" — read on every alt link to enforce the
            // one-level rule, and what 7.4 will group the roster by.
            entity.HasIndex(e => new { e.TenantId, e.MainRosterEntryId });

            // EF also generates single-column indexes on CharacterId and TenantRankId to back the two
            // foreign keys, and those do NOT start with TenantId. They are deliberate exceptions
            // rather than oversights: a foreign-key constraint is not tenant-aware, so when SQL Server
            // deletes a Character or a TenantRank it has to prove no RosterEntry in ANY tenant still
            // references it, and a TenantId-first composite cannot serve that check. Both deletions
            // are real paths here — erasure for the first, rank management for the second — so these
            // stay. No application query uses either; every one of those filters on TenantId first.
        });

        // Tenant-scoped (tenancy.md), and the second row after RosterEntry to hold an FK into the
        // global Character table — the allowed direction.
        modelBuilder.Entity<CharacterClaim>(entity =>
        {
            entity.Property(c => c.UserId).IsRequired();

            entity.HasOne<Tenant>()
                .WithMany()
                .HasForeignKey(c => c.TenantId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(c => c.Character)
                .WithMany()
                .HasForeignKey(c => c.CharacterId)
                // Erasure is an explicit, application-orchestrated routine (external.md) that removes
                // every tenant's claims for a character by hand, not a DB cascade.
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(c => c.UserId)
                // Cascade, matching TenantMembership: a deleted account's claims are meaningless, and
                // unlike character erasure there is no cross-tenant routine that needs to see them
                // first.
                .OnDelete(DeleteBehavior.Cascade);

            // At most one claim per character per community — the one-claim rule at rest, and the
            // authority when two members race past Business's pre-check. Starts with TenantId like
            // every tenant-scoped index.
            entity.HasIndex(c => new { c.TenantId, c.CharacterId }).IsUnique();

            // "Which characters has this member claimed", which 7.3's alt linking needs to answer
            // ("a member may link alts only among entries THEY claim").
            entity.HasIndex(c => new { c.TenantId, c.UserId });
        });

        // Tenant-scoped (tenancy.md): what THIS community calls a guild's in-game ranks. Blizzard does
        // not expose guild rank names, so these are typed by an officer — which is what keeps them out
        // of the global zone, whatever they describe. See GuildRankName for the full reasoning.
        modelBuilder.Entity<GuildRankName>(entity =>
        {
            entity.Property(r => r.Name).HasMaxLength(32).IsRequired();

            entity.HasOne<Tenant>()
                .WithMany()
                .HasForeignKey(r => r.TenantId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(r => r.Guild)
                .WithMany()
                .HasForeignKey(r => r.GuildId)
                // The global guild outlives this community's names for its ranks, and erasure is an
                // explicit routine rather than a DB cascade (tenancy.md).
                .OnDelete(DeleteBehavior.Restrict);

            // One name per rank per guild per community. Starts with TenantId like every tenant-scoped
            // index, and is what makes the officer's write an upsert rather than a duplicate factory.
            entity.HasIndex(r => new { r.TenantId, r.GuildId, r.Rank }).IsUnique();
        });

        // Tenant-scoped (tenancy.md). Write-only from the app's point of view — 14.2 adds the
        // officer-visible read, and audit rows are never editable or deletable from the app.
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.Property(a => a.ActorUserId).IsRequired();
            entity.Property(a => a.TargetType).HasMaxLength(64).IsRequired();
            entity.Property(a => a.Before).HasMaxLength(1000);
            entity.Property(a => a.After).HasMaxLength(1000);

            // Stored as the enum's NAME, not its number. Audit rows outlive the code that wrote them,
            // so an integer would make reordering or inserting an AuditAction member a silent
            // rewriting of history — the one thing an audit table must never permit.
            entity.Property(a => a.Action).HasConversion<string>().HasMaxLength(64).IsRequired();

            entity.HasOne<Tenant>()
                .WithMany()
                .HasForeignKey(a => a.TenantId)
                .OnDelete(DeleteBehavior.Restrict);

            // ActorUserId, SubjectUserId, TargetType and TargetId are deliberately NOT foreign keys.
            // An audit row has to survive the deletion of whatever it describes — an FK would either
            // block that deletion or cascade the evidence away with it, and "the account was deleted"
            // is exactly when the record matters most.

            // 14.2's screen reads a tenant's rows newest-first.
            entity.HasIndex(a => new { a.TenantId, a.OccurredAt });
        });

        // Global reference zone (tenancy.md) — public Blizzard data, identical for every tenant.
        // No TenantId, no query filter: these types simply don't implement ITenantScoped, so the
        // convention loop below skips them automatically.
        modelBuilder.Entity<Realm>(entity =>
        {
            entity.Property(r => r.Slug).HasMaxLength(64).IsRequired();
            entity.Property(r => r.Name).HasMaxLength(200).IsRequired();
            entity.Property(r => r.Region).HasMaxLength(8).IsRequired();

            entity.HasIndex(r => new { r.Region, r.Slug }).IsUnique();

            // The stable per-row source id (6.4b), which is what lets a renamed realm update in place
            // instead of appearing as a second realm with orphaned characters — the same job
            // Character.BlizzardCharacterId does.
            //
            // Filtered, unlike the other source-id indexes in this file, because 0 is a legitimate
            // "not known yet": the seeded demo realms are fictional and have no Blizzard id, and any
            // realm row written before the catalogue sync ran has none either. A plain unique index
            // would make a second such row a crash rather than a normal state.
            entity.HasIndex(r => r.BlizzardRealmId)
                .IsUnique()
                .HasFilter("[BlizzardRealmId] <> 0");
        });

        modelBuilder.Entity<Character>(entity =>
        {
            entity.Property(c => c.Name).HasMaxLength(64).IsRequired();
            entity.Property(c => c.NameLower).HasMaxLength(64).IsRequired();
            entity.Property(c => c.Spec).HasMaxLength(32);

            entity.HasOne(c => c.Realm)
                .WithMany()
                .HasForeignKey(c => c.RealmId)
                // Erasure is an explicit, application-orchestrated routine (tenancy.md), not a DB cascade.
                .OnDelete(DeleteBehavior.Restrict);

            // The natural key (RESTRICTION, 3.1): Blizzard character ids don't survive renames/transfers.
            entity.HasIndex(c => new { c.RealmId, c.NameLower }).IsUnique();

            // The erasure target (backend.md), unique the same way Realm.BlizzardConnectedRealmId and
            // Item.BlizzardItemId are — not the lookup key (see above), but still one id per row.
            entity.HasIndex(c => c.BlizzardCharacterId).IsUnique();

            // Supports the sync worker's stale-row query (6.5), which both filters and orders on this
            // column. Without it every run scans the whole character table — tolerable at seed volume,
            // and exactly the kind of thing that is only noticed once the table is large enough that
            // the compliance job has become the slowest thing in the system.
            entity.HasIndex(c => c.LastSyncedAt);
        });

        modelBuilder.Entity<CharacterEquipment>(entity =>
        {
            entity.HasOne(e => e.Character)
                .WithOne(c => c.Equipment)
                .HasForeignKey<CharacterEquipment>(e => e.CharacterId)
                .OnDelete(DeleteBehavior.Restrict);

            // One equipment snapshot per character.
            entity.HasIndex(e => e.CharacterId).IsUnique();
        });

        modelBuilder.Entity<EquippedItem>(entity =>
        {
            entity.Property(i => i.ItemName).HasMaxLength(200).IsRequired();
            entity.Property(i => i.IconName).HasMaxLength(200);

            entity.HasOne(i => i.CharacterEquipment)
                .WithMany(e => e.EquippedItems)
                .HasForeignKey(i => i.CharacterEquipmentId)
                .OnDelete(DeleteBehavior.Restrict);

            // One item per slot.
            entity.HasIndex(i => new { i.CharacterEquipmentId, i.Slot }).IsUnique();
        });

        modelBuilder.Entity<Item>(entity =>
        {
            entity.Property(i => i.Name).HasMaxLength(200).IsRequired();
            entity.Property(i => i.SearchText).HasMaxLength(500).IsRequired();

            entity.HasIndex(i => i.BlizzardItemId).IsUnique();
        });

        modelBuilder.Entity<Profession>(entity =>
        {
            entity.Property(p => p.Name).HasMaxLength(100).IsRequired();

            entity.HasIndex(p => p.BlizzardProfessionId).IsUnique();
        });

        modelBuilder.Entity<Recipe>(entity =>
        {
            entity.Property(r => r.Name).HasMaxLength(200).IsRequired();

            entity.HasOne(r => r.Profession)
                .WithMany()
                .HasForeignKey(r => r.ProfessionId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(r => r.CraftedItem)
                .WithMany()
                .HasForeignKey(r => r.CraftedItemId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(r => r.BlizzardRecipeId).IsUnique();
        });

        modelBuilder.Entity<ReagentSlot>(entity =>
        {
            entity.HasOne(s => s.Recipe)
                .WithMany()
                .HasForeignKey(s => s.RecipeId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(s => s.Item)
                .WithMany()
                .HasForeignKey(s => s.ItemId)
                .OnDelete(DeleteBehavior.Restrict);

            // A recipe lists a given reagent item once, with a quantity.
            entity.HasIndex(s => new { s.RecipeId, s.ItemId }).IsUnique();
        });

        modelBuilder.Entity<Guild>(entity =>
        {
            entity.Property(g => g.Name).HasMaxLength(64).IsRequired();
            entity.Property(g => g.NameLower).HasMaxLength(64).IsRequired();

            entity.HasOne(g => g.Realm)
                .WithMany()
                .HasForeignKey(g => g.RealmId)
                .OnDelete(DeleteBehavior.Restrict);

            // Same trap as Character (3.1): Blizzard looks a guild up by realm + name, so that's
            // the natural key, not the Blizzard guild id.
            entity.HasIndex(g => new { g.RealmId, g.NameLower }).IsUnique();
        });

        modelBuilder.Entity<GuildMember>(entity =>
        {
            entity.HasOne(m => m.Guild)
                .WithMany()
                .HasForeignKey(m => m.GuildId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(m => m.Character)
                .WithMany()
                .HasForeignKey(m => m.CharacterId)
                .OnDelete(DeleteBehavior.Restrict);

            // A character appears once in a given guild's roster.
            entity.HasIndex(m => new { m.GuildId, m.CharacterId }).IsUnique();
        });

        // By convention, not per entity (tenancy.md) — every current and future ITenantScoped entity
        // picks this up automatically. Tenant and TenantMembership are deliberately NOT ITenantScoped;
        // see the comment on TenantMembership for why.
        modelBuilder.ApplyTenantScopedQueryFilters(() => tenantContext.TenantId);
    }
}
