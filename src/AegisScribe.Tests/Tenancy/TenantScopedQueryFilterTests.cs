using AegisScribe.Domain.Context;
using AegisScribe.Domain.Data;
using AegisScribe.Domain.Managers.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace AegisScribe.Tests.Tenancy;

// A minimal fixture entity/DbContext exercising the actual production convention-loop code
// (TenantScopedModelBuilderExtensions.ApplyTenantScopedQueryFilters), not a re-implementation of it.
// EF Core InMemory, not the real AppHost/SQL Server fixture — this tests the filter mechanism itself.
internal class FixtureThing : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = null!;
}

internal class FixtureDbContext(DbContextOptions<FixtureDbContext> options, ITenantContext tenantContext)
    : DbContext(options)
{
    public DbSet<FixtureThing> Things => Set<FixtureThing>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FixtureThing>();
        modelBuilder.ApplyTenantScopedQueryFilters(() => tenantContext.TenantId);
    }
}

public class TenantScopedQueryFilterTests
{
    private static FixtureDbContext CreateContext(string databaseName, TenantContext tenantContext)
    {
        var options = new DbContextOptionsBuilder<FixtureDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        return new FixtureDbContext(options, tenantContext);
    }

    [Fact]
    public async Task TwoInstances_EachSeeOnlyTheirOwnTenantsRows()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var contextATenantContext = new TenantContext();
        contextATenantContext.SetTenant(tenantA);
        await using (var contextA = CreateContext(databaseName, contextATenantContext))
        {
            contextA.Things.Add(new FixtureThing { Id = Guid.NewGuid(), TenantId = tenantA, Name = "A's thing" });
            await contextA.SaveChangesAsync();
        }

        var contextBTenantContext = new TenantContext();
        contextBTenantContext.SetTenant(tenantB);
        await using (var contextB = CreateContext(databaseName, contextBTenantContext))
        {
            contextB.Things.Add(new FixtureThing { Id = Guid.NewGuid(), TenantId = tenantB, Name = "B's thing" });
            await contextB.SaveChangesAsync();
        }

        // Re-open each context fresh (a THIRD and FOURTH instance) against the same underlying store,
        // proving the filter is evaluated per-instance despite OnModelCreating/the compiled model
        // only ever running once for the FixtureDbContext type — the regression this test exists for.
        var readTenantAContext = new TenantContext();
        readTenantAContext.SetTenant(tenantA);
        await using var readAsA = CreateContext(databaseName, readTenantAContext);
        var visibleToA = await readAsA.Things.ToListAsync();
        Assert.Single(visibleToA);
        Assert.Equal("A's thing", visibleToA[0].Name);

        var readTenantBContext = new TenantContext();
        readTenantBContext.SetTenant(tenantB);
        await using var readAsB = CreateContext(databaseName, readTenantBContext);
        var visibleToB = await readAsB.Things.ToListAsync();
        Assert.Single(visibleToB);
        Assert.Equal("B's thing", visibleToB[0].Name);
    }

    [Fact]
    public async Task UnresolvedTenant_ThrowsOnQuery()
    {
        var tenantContext = new TenantContext();
        await using var context = CreateContext(Guid.NewGuid().ToString(), tenantContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.Things.ToListAsync());
    }
}
