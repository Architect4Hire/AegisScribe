using AegisScribe.Domain.Context;
using AegisScribe.Domain.Data;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Tests.Auth;
using Aspire.Hosting.Testing;
using Microsoft.EntityFrameworkCore;

namespace AegisScribe.Tests.Tenancy;

// There's no tenant-creation endpoint yet (that's future work), so tests that need a real Tenant/
// TenantMembership row reach the database directly, the same way the migration service does —
// through a plain AegisScribeDbContext against the real aegisscribedb connection string, since the
// "api" resource runs in its own process and its DI container isn't reachable from the test process.
internal static class TenantSeeding
{
    public static string UniqueSlug() => $"tenant-{Guid.NewGuid():N}";

    public static async Task<Tenant> CreateTenantAsync(AegisScribeAppFixture fixture, string? slug = null, string? name = null)
    {
        await using var db = await OpenDbContextAsync(fixture);
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Slug = slug ?? UniqueSlug(),
            Name = name ?? "Test Tenant",
            TimeZoneId = "UTC",
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant;
    }

    public static async Task AddMembershipAsync(
        AegisScribeAppFixture fixture, Guid tenantId, string userId, TenantRole role = TenantRole.Member)
    {
        await using var db = await OpenDbContextAsync(fixture);
        db.TenantMemberships.Add(new TenantMembership
        {
            TenantId = tenantId,
            UserId = userId,
            Role = role,
            JoinedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    public static async Task<AegisScribeDbContext> OpenDbContextAsync(AegisScribeAppFixture fixture)
    {
        var connectionString = await fixture.App.GetConnectionStringAsync("aegisscribedb");
        var options = new DbContextOptionsBuilder<AegisScribeDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        // Tenant/TenantMembership are deliberately not ITenantScoped (2.4), so the query filter never
        // applies to this seeding path — an always-unresolved TenantContext is correct here.
        return new AegisScribeDbContext(options, new TenantContext());
    }
}
