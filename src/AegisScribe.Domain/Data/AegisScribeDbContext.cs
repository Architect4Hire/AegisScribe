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
    public DbSet<TenantInvitation> TenantInvitations => Set<TenantInvitation>();
    public DbSet<TenantJoinRequest> TenantJoinRequests => Set<TenantJoinRequest>();
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

    // Tenant-scoped entities are not configured with a query filter individually — the convention loop
    // at the end of this method applies one to every ITenantScoped type. Deletion of a tenant, and
    // erasure of a character, are explicit application-orchestrated routines (tenancy.md), which is
    // why the relationships below are Restrict rather than Cascade.
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
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(m => m.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Serves "list my tenants" (GET /api/v1/me), which isn't filtered by tenant first.
            entity.HasIndex(m => m.UserId);
        });

        // The two membership-lifecycle tables. Both carry TenantId and neither is ITenantScoped, for
        // the reason spelled out on each entity: the person on the other side of both is NOT a member
        // yet, so there is no resolved tenant to filter by or stamp from.
        modelBuilder.Entity<TenantInvitation>(entity =>
        {
            entity.Property(i => i.TokenHash).HasMaxLength(32).IsRequired();
            entity.Property(i => i.Note).HasMaxLength(100);
            entity.Property(i => i.CreatedByUserId).IsRequired();

            entity.HasOne<Tenant>()
                .WithMany()
                .HasForeignKey(i => i.TenantId)
                .OnDelete(DeleteBehavior.Restrict);

            // NOT TenantId-first, and that is the exception the whole design rests on: acceptance
            // looks a row up by token alone, with no tenant in hand, and the tenant it lands in is
            // whatever this row says. Unique across every community, so one token can never name two.
            entity.HasIndex(i => i.TokenHash).IsUnique();

            // The officer-facing list, which does have a tenant.
            entity.HasIndex(i => new { i.TenantId, i.CreatedAt });
        });

        modelBuilder.Entity<TenantJoinRequest>(entity =>
        {
            entity.Property(r => r.UserId).IsRequired();
            entity.Property(r => r.Message).HasMaxLength(500);

            // Stored as the NAME, so inserting an enum member never rewrites decided history.
            entity.Property(r => r.Status).HasConversion<string>().HasMaxLength(16).IsRequired();

            entity.HasOne<Tenant>()
                .WithMany()
                .HasForeignKey(r => r.TenantId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // One PENDING request per person per community — filtered, so a declined request stays as
            // history and the same person may ask again later. The filter is what makes this a rule
            // about outstanding requests rather than a one-shot-forever ban, and it is the authority
            // under a race: Business pre-checks for the readable answer, this decides it.
            entity.HasIndex(r => new { r.TenantId, r.UserId })
                .IsUnique()
                .HasFilter($"[{nameof(TenantJoinRequest.Status)}] = '{nameof(JoinRequestStatus.Pending)}'");

            // The officer's queue.
            entity.HasIndex(r => new { r.TenantId, r.Status, r.RequestedAt });
        });

        modelBuilder.Entity<TenantSyncBudgetWindow>(entity =>
        {
            entity.HasOne<Tenant>()
                .WithMany()
                .HasForeignKey(w => w.TenantId)
                .OnDelete(DeleteBehavior.Restrict);

            // Load-bearing rather than tidy: SyncBudgetRepository's atomic consume relies on a
            // duplicate insert being REJECTED when two first-ever requests race, rather than producing
            // two windows that each grant a full budget.
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

            // A community links a given guild once. TenantId-first like every tenant-scoped index.
            entity.HasIndex(g => new { g.TenantId, g.GuildId }).IsUnique();
        });

        modelBuilder.Entity<TenantRank>(entity =>
        {
            entity.Property(r => r.Name).HasMaxLength(32).IsRequired();

            // Exactly "#rrggbb". The width guards against a tenant-configured colour becoming CSS
            // injected into the rank pill's --rank-color; RankValidationRules enforces the shape.
            entity.Property(r => r.Colour).HasMaxLength(7).IsRequired();

            entity.HasOne<Tenant>()
                .WithMany()
                .HasForeignKey(r => r.TenantId)
                .OnDelete(DeleteBehavior.Restrict);

            // Decides the race when two officers add "Raider" at once — TenantRankBusiness only
            // pre-checks it. Case-insensitive by the column's collation, deliberately.
            entity.HasIndex(r => new { r.TenantId, r.Name }).IsUnique();
        });

        // Where the two zones meet: an FK INTO the global Character table, and that direction only. A
        // global entity holding one back would make shared reference data depend on one community.
        modelBuilder.Entity<RosterEntry>(entity =>
        {
            entity.Property(e => e.OfficerNote).HasMaxLength(1000);

            entity.HasOne<Tenant>()
                .WithMany()
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Character)
                .WithMany()
                .HasForeignKey(e => e.CharacterId)
                .OnDelete(DeleteBehavior.Restrict);

            // Self-referencing, one level deep. Restrict has a consequence worth knowing: removing an
            // entry other entries call their main FAILS until those alts are detached, so the remove
            // path has to handle it deliberately.
            entity.HasOne(e => e.MainRosterEntry)
                .WithMany()
                .HasForeignKey(e => e.MainRosterEntryId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.TenantRank)
                .WithMany()
                .HasForeignKey(e => e.TenantRankId)
                // Restrict, not SetNull: clearing a rank off every holder is the silent kind of
                // damage. TenantRankBusiness refuses with a 409 first, so this backstops a race.
                .OnDelete(DeleteBehavior.Restrict);

            // A character sits on a given community's roster once.
            entity.HasIndex(e => new { e.TenantId, e.CharacterId }).IsUnique();

            // What the rank-in-use count reads; without it a rank delete scans the whole roster.
            entity.HasIndex(e => new { e.TenantId, e.TenantRankId });

            // "Does anything call this entry its main" — read on every alt link, and what the roster
            // groups by.
            entity.HasIndex(e => new { e.TenantId, e.MainRosterEntryId });

            // EF also generates single-column indexes backing the two foreign keys, and those do NOT
            // start with TenantId. Deliberate: a foreign-key constraint is not tenant-aware, so
            // deleting a Character or TenantRank means proving no RosterEntry in ANY tenant references
            // it, which a TenantId-first composite cannot serve. No application query uses either.
        });

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
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(c => c.UserId)
                // Cascade, matching TenantMembership: a deleted account's claims are meaningless, and
                // unlike character erasure no cross-tenant routine needs to see them first.
                .OnDelete(DeleteBehavior.Cascade);

            // The one-claim rule at rest, and the authority when two members race past the pre-check.
            entity.HasIndex(c => new { c.TenantId, c.CharacterId }).IsUnique();

            // "Which characters has this member claimed" — what alt linking needs to answer.
            entity.HasIndex(c => new { c.TenantId, c.UserId });
        });

        // Blizzard does not expose guild rank names, so these are typed by an officer — which is what
        // keeps them out of the global zone.
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
                .OnDelete(DeleteBehavior.Restrict);

            // What makes the officer's write an upsert rather than a duplicate factory.
            entity.HasIndex(r => new { r.TenantId, r.GuildId, r.Rank }).IsUnique();
        });

        // Write-only from the app's point of view; audit rows are never editable or deletable.
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.Property(a => a.ActorUserId).IsRequired();
            entity.Property(a => a.TargetType).HasMaxLength(64).IsRequired();
            entity.Property(a => a.Before).HasMaxLength(1000);
            entity.Property(a => a.After).HasMaxLength(1000);

            // The enum's NAME, not its number: audit rows outlive the code that wrote them, so an
            // integer would make reordering an AuditAction member a silent rewriting of history.
            entity.Property(a => a.Action).HasConversion<string>().HasMaxLength(64).IsRequired();

            entity.HasOne<Tenant>()
                .WithMany()
                .HasForeignKey(a => a.TenantId)
                .OnDelete(DeleteBehavior.Restrict);

            // ActorUserId, SubjectUserId, TargetType and TargetId are deliberately NOT foreign keys: an
            // audit row has to survive the deletion of whatever it describes, and an FK would either
            // block that deletion or cascade the evidence away with it.
            entity.HasIndex(a => new { a.TenantId, a.OccurredAt });
        });

        // Global reference zone (tenancy.md) from here down — public Blizzard data, identical for every
        // tenant. These types don't implement ITenantScoped, so the convention loop skips them.
        modelBuilder.Entity<Realm>(entity =>
        {
            entity.Property(r => r.Slug).HasMaxLength(64).IsRequired();
            entity.Property(r => r.Name).HasMaxLength(200).IsRequired();
            entity.Property(r => r.Region).HasMaxLength(8).IsRequired();

            entity.HasIndex(r => new { r.Region, r.Slug }).IsUnique();

            // The stable per-row source id, which lets a renamed realm update in place rather than
            // appear as a second realm with orphaned characters. Filtered, unlike the other source-id
            // indexes here, because 0 is a legitimate "not known yet" — the seeded demo realms are
            // fictional, and a plain unique index would make a second such row a crash.
            entity.HasIndex(r => r.BlizzardRealmId)
                .IsUnique()
                .HasFilter("[BlizzardRealmId] <> 0");
        });

        modelBuilder.Entity<Character>(entity =>
        {
            entity.Property(c => c.Name).HasMaxLength(64).IsRequired();
            entity.Property(c => c.NameLower).HasMaxLength(64).IsRequired();
            entity.Property(c => c.Spec).HasMaxLength(32);
            entity.Property(c => c.AvatarUrl).HasMaxLength(500);
            entity.Property(c => c.RenderUrl).HasMaxLength(500);

            // The media backfill's selection: characters never asked, or asked too long ago.
            entity.HasIndex(c => c.MediaSyncedAt);

            entity.HasOne(c => c.Realm)
                .WithMany()
                .HasForeignKey(c => c.RealmId)
                .OnDelete(DeleteBehavior.Restrict);

            // The natural key: Blizzard character ids don't survive renames or transfers.
            entity.HasIndex(c => new { c.RealmId, c.NameLower }).IsUnique();

            // The erasure target — not the lookup key, but still one id per row.
            entity.HasIndex(c => c.BlizzardCharacterId).IsUnique();

            // The sync worker's stale-row query both filters and orders on this column; without the
            // index every run scans the whole character table.
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

            // The icon backfill groups by item id and the equipment write looks icons up by it.
            entity.HasIndex(i => i.BlizzardItemId);

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

            // Same trap as Character: Blizzard looks a guild up by realm + name, not by guild id.
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
