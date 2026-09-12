using AegisScribe.Domain.Context;
using AegisScribe.Domain.Data;
using AegisScribe.Domain.Managers.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace AegisScribe.Tests.Tenancy;

// Reuses FixtureThing/FixtureDbContext from TenantScopedQueryFilterTests.cs (same assembly, internal)
// so this exercises the same production fixture, wired with the interceptor under test.
public class TenantStampingInterceptorTests
{
    private static FixtureDbContext CreateContext(string databaseName, TenantContext tenantContext)
    {
        var options = new DbContextOptionsBuilder<FixtureDbContext>()
            .UseInMemoryDatabase(databaseName)
            .AddInterceptors(new TenantStampingInterceptor(tenantContext))
            .Options;
        return new FixtureDbContext(options, tenantContext);
    }

    [Fact]
    public async Task AddedEntity_WithNoTenantIdSet_IsStampedWithTheAmbientTenant()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantA);

        await using var context = CreateContext(databaseName, tenantContext);
        var thing = new FixtureThing { Id = Guid.NewGuid(), TenantId = Guid.Empty, Name = "unstamped" };
        context.Things.Add(thing);

        await context.SaveChangesAsync();

        Assert.Equal(tenantA, thing.TenantId);
    }

    [Fact]
    public async Task AddedEntity_WithADifferentTenantsId_ThrowsAndPersistsNothing()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantA);

        await using var context = CreateContext(databaseName, tenantContext);
        context.Things.Add(new FixtureThing { Id = Guid.NewGuid(), TenantId = tenantB, Name = "cross-tenant insert" });

        await Assert.ThrowsAsync<TenantStampMismatchException>(() => context.SaveChangesAsync());

        // Confirm nothing landed in the store — read back as tenant A, whose filter would show its own row.
        var readContext = new TenantContext();
        readContext.SetTenant(tenantA);
        await using var verify = CreateContext(databaseName, readContext);
        Assert.Empty(await verify.Things.ToListAsync());
    }

    [Fact]
    public async Task ModifiedEntity_MovedToADifferentTenantsId_Throws()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var seedContext = new TenantContext();
        seedContext.SetTenant(tenantA);
        var thingId = Guid.NewGuid();
        await using (var seed = CreateContext(databaseName, seedContext))
        {
            seed.Things.Add(new FixtureThing { Id = thingId, TenantId = tenantA, Name = "A's thing" });
            await seed.SaveChangesAsync();
        }

        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantA);
        await using var context = CreateContext(databaseName, tenantContext);
        var tracked = await context.Things.SingleAsync(t => t.Id == thingId);
        tracked.TenantId = tenantB;

        await Assert.ThrowsAsync<TenantStampMismatchException>(() => context.SaveChangesAsync());
    }
}
