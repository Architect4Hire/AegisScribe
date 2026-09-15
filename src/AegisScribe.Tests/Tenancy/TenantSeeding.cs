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

    /// <summary>
    /// A context scoped to one tenant, for tests that touch a genuinely <c>ITenantScoped</c> entity.
    /// <para>
    /// The unresolved context above cannot read those at all: their global query filter dereferences
    /// <c>ITenantContext.TenantId</c> and <see cref="TenantContext"/> throws rather than substituting a
    /// default — which is the filter working, not a gap to route around. This stands in for what the
    /// tenant-resolution middleware does for a real request, and nothing else.
    /// </para>
    /// </summary>
    public static async Task<AegisScribeDbContext> OpenDbContextAsync(AegisScribeAppFixture fixture, Guid tenantId)
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId);

        var connectionString = await fixture.App.GetConnectionStringAsync("aegisscribedb");
        var options = new DbContextOptionsBuilder<AegisScribeDbContext>()
            .UseSqlServer(connectionString)
            // The interceptor matters here and not on the unresolved helper above: business code never
            // assigns TenantId by hand (tenancy.md), so an ITenantScoped entity inserted through a
            // context WITHOUT the interceptor keeps TenantId as Guid.Empty and fails the foreign key.
            // Production wires this through DI in Program.cs; a test context that skipped it would not
            // be exercising the same write path.
            .AddInterceptors(new TenantStampingInterceptor(tenantContext))
            .Options;

        return new AegisScribeDbContext(options, tenantContext);
    }
}
