using AegisScribe.Domain.Data;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Tests.Auth;
using AegisScribe.Tests.Tenancy;

namespace AegisScribe.Tests.Roster;

// Repository: real SQL Server through the Aspire fixture, because two of the three claims here only
// exist in the database — the collation that makes the unique name check case-insensitive, and the
// index violation the pre-check cannot close.
[Collection("AegisScribe API")]
public class TenantRankRepositoryTests(AegisScribeAppFixture fixture)
{
    [Fact]
    public async Task List_OrdersBySortOrder_ThenByNameToBreakTies()
    {
        var tenant = await TenantSeeding.CreateTenantAsync(fixture);
        await SeedAsync(tenant.Id, ("Social", 30, "#3e9c77"), ("Raider", 10, "#cba76a"), ("Officer", 10, "#3d9bff"));

        await using var db = await TenantSeeding.OpenDbContextAsync(fixture, tenant.Id);
        var ranks = await new TenantRankRepository(db).ListAsync(CancellationToken.None);

        // Officer before Raider at the same position — the tie-break, without which the two swap
        // places between reads and the roster looks unstable.
        Assert.Equal(new[] { "Officer", "Raider", "Social" }, ranks.Select(r => r.Name).ToArray());
        Assert.Equal("#cba76a", ranks[1].Colour);
    }

    [Fact]
    public async Task NameExists_IsCaseInsensitive_AndCanExcludeTheRowBeingEdited()
    {
        var tenant = await TenantSeeding.CreateTenantAsync(fixture);
        var seeded = await SeedAsync(tenant.Id, ("Raider", 10, "#cba76a"));
        var rankId = seeded.Single().Id;

        await using var db = await TenantSeeding.OpenDbContextAsync(fixture, tenant.Id);
        var repository = new TenantRankRepository(db);

        // "raider" and "Raider" are the same rank to the people reading the roster, and the unique
        // index agrees — so the pre-check has to ask the same question the index answers.
        Assert.True(await repository.NameExistsAsync("raider", excludingId: null, CancellationToken.None));
        Assert.False(await repository.NameExistsAsync("raider", excludingId: rankId, CancellationToken.None));
        Assert.False(await repository.NameExistsAsync("Trial", excludingId: null, CancellationToken.None));
    }

    [Fact]
    public async Task Add_LosingTheNameRace_SurfacesAsRankNameTaken_NotADbUpdateException()
    {
        // The race TenantRankBusiness's pre-check cannot close: two officers both find "Raider" free,
        // and the index decides. Simulated by writing directly, bypassing the pre-check entirely.
        var tenant = await TenantSeeding.CreateTenantAsync(fixture);
        await SeedAsync(tenant.Id, ("Raider", 10, "#cba76a"));

        await using var db = await TenantSeeding.OpenDbContextAsync(fixture, tenant.Id);
        var repository = new TenantRankRepository(db);

        var ex = await Assert.ThrowsAsync<RankNameTakenException>(() => repository.AddAsync(
            new TenantRank { Id = Guid.NewGuid(), Name = "Raider", SortOrder = 20, Colour = "#616d7e" },
            CancellationToken.None));

        Assert.Equal("Raider", ex.Name);
    }

    [Fact]
    public async Task TwoCommunitiesMayEachHaveARaider()
    {
        // The unique index starts with TenantId, so uniqueness is per community. If it were on Name
        // alone, the first community to define "Raider" would take the name platform-wide.
        var tenantA = await TenantSeeding.CreateTenantAsync(fixture);
        var tenantB = await TenantSeeding.CreateTenantAsync(fixture);

        await SeedAsync(tenantA.Id, ("Raider", 10, "#cba76a"));
        await SeedAsync(tenantB.Id, ("Raider", 10, "#3e9c77"));

        await using var db = await TenantSeeding.OpenDbContextAsync(fixture, tenantB.Id);
        var ranks = await new TenantRankRepository(db).ListAsync(CancellationToken.None);

        Assert.Equal("#3e9c77", Assert.Single(ranks).Colour);
    }

    [Fact]
    public async Task ARankCarryingAnotherCommunitysTenantId_IsRefusedAndPersistsNothing()
    {
        // tenancy.md's third required assertion: a write from A cannot set a row's TenantId to B. No
        // request shape can even express it — neither rank ViewModel has a TenantId field — so this
        // pins the layer below that, where the only remaining way in would be domain code assigning
        // TenantId by hand. TenantStampingInterceptorTests proves the interceptor's behaviour on a
        // fixture entity; this proves TenantRank actually travels that path, against real SQL Server.
        var tenantA = await TenantSeeding.CreateTenantAsync(fixture);
        var tenantB = await TenantSeeding.CreateTenantAsync(fixture);

        await using var db = await TenantSeeding.OpenDbContextAsync(fixture, tenantA.Id);
        db.TenantRanks.Add(new TenantRank
        {
            Id = Guid.NewGuid(),
            TenantId = tenantB.Id,
            Name = "Smuggled",
            SortOrder = 10,
            Colour = "#ffffff",
        });

        await Assert.ThrowsAsync<TenantStampMismatchException>(() => db.SaveChangesAsync());

        await using var verify = await TenantSeeding.OpenDbContextAsync(fixture, tenantB.Id);
        Assert.Empty(await new TenantRankRepository(verify).ListAsync(CancellationToken.None));
    }

    private async Task<IReadOnlyList<TenantRank>> SeedAsync(
        Guid tenantId, params (string Name, int SortOrder, string Colour)[] ranks)
    {
        await using var db = await TenantSeeding.OpenDbContextAsync(fixture, tenantId);

        var entities = ranks
            .Select(rank => new TenantRank
            {
                Id = Guid.NewGuid(),
                Name = rank.Name,
                SortOrder = rank.SortOrder,
                Colour = rank.Colour,
            })
            .ToList();

        // TenantId is left unset: the interceptor on this context stamps it, exactly as it does for a
        // real request (TenantSeeding.OpenDbContextAsync wires it in for that reason).
        db.TenantRanks.AddRange(entities);
        await db.SaveChangesAsync();

        return entities;
    }
}
