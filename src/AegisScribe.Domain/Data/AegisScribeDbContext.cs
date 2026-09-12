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

        // By convention, not per entity (tenancy.md) — every current and future ITenantScoped entity
        // picks this up automatically. Tenant and TenantMembership are deliberately NOT ITenantScoped;
        // see the comment on TenantMembership for why.
        modelBuilder.ApplyTenantScopedQueryFilters(() => tenantContext.TenantId);
    }
}
