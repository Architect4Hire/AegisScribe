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

        // Global reference zone (tenancy.md) — public Blizzard data, identical for every tenant.
        // No TenantId, no query filter: these types simply don't implement ITenantScoped, so the
        // convention loop below skips them automatically.
        modelBuilder.Entity<Realm>(entity =>
        {
            entity.Property(r => r.Slug).HasMaxLength(64).IsRequired();
            entity.Property(r => r.Name).HasMaxLength(200).IsRequired();
            entity.Property(r => r.Region).HasMaxLength(8).IsRequired();

            entity.HasIndex(r => new { r.Region, r.Slug }).IsUnique();
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
