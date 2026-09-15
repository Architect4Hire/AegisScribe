using AegisScribe.Domain.Data;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Tests.Auth;
using AegisScribe.Tests.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace AegisScribe.Tests.Character;

// 6.5's selection query, against real SQL Server. The LEFT JOIN to equipment and the ordering are both
// things an in-memory provider would happily get wrong in a way that still passed.
[Collection("AegisScribe API")]
public class StaleCharacterQueryTests(AegisScribeAppFixture fixture)
{
    // The fixture database is shared and accumulates rows from every run, and this query orders
    // oldest-first across the whole table — so no test here may assume its subject lands on a page.
    // Every assertion about WHICH rows come back asks for the entire stale set; paging has its own test.
    private const int WholeStaleSet = 100_000;

    [Fact]
    public async Task FindStale_ReturnsCharactersPastTheCutoffAndLeavesFreshOnesAlone()
    {
        var realm = await CharacterSeeding.CreateRealmAsync(fixture);
        var stale = await SeedAsync(realm, syncedAt: DateTimeOffset.UtcNow.AddDays(-20));
        var fresh = await SeedAsync(realm, syncedAt: DateTimeOffset.UtcNow);

        var due = await FindStaleAsync(DateTimeOffset.UtcNow.AddDays(-7), take: WholeStaleSet);

        Assert.Contains(due, c => c.Id == stale.Id);
        Assert.DoesNotContain(due, c => c.Id == fresh.Id);
    }

    [Fact]
    public async Task FindStale_CarriesEverythingAProfileRequestNeeds()
    {
        var realm = await CharacterSeeding.CreateRealmAsync(fixture, region: "eu");
        var character = await SeedAsync(realm, syncedAt: DateTimeOffset.UtcNow.AddDays(-20));

        var due = await FindStaleAsync(DateTimeOffset.UtcNow.AddDays(-7), take: WholeStaleSet);

        var row = Assert.Single(due, c => c.Id == character.Id);
        Assert.Equal("eu", row.Region);
        Assert.Equal(realm.Slug, row.RealmSlug);
        Assert.Equal(character.Name, row.Name);

        // The realm id comes along so the upsert does not have to resolve it again.
        Assert.Equal(realm.Id, row.RealmId);
    }

    [Fact]
    public async Task FindStale_IncludesACharacterWhoseGearWasNeverFetched()
    {
        // The LEFT JOIN case. An INNER JOIN here would silently exclude exactly the characters most in
        // need of a sync — the ones with no equipment row at all.
        var realm = await CharacterSeeding.CreateRealmAsync(fixture);
        var character = await SeedAsync(realm, syncedAt: DateTimeOffset.UtcNow, withEquipment: false);

        var due = await FindStaleAsync(DateTimeOffset.UtcNow.AddDays(-7), take: WholeStaleSet);

        Assert.Contains(due, c => c.Id == character.Id);
    }

    [Fact]
    public async Task FindStale_IncludesAFreshCharacterWhoseGearHasAgedOut()
    {
        // Equipment is a separate endpoint with a separate LastSyncedAt and its own thirty-day
        // obligation. A failed gear fetch leaves a fresh character row that a character-only predicate
        // would never select again.
        var realm = await CharacterSeeding.CreateRealmAsync(fixture);
        var character = await SeedAsync(realm, syncedAt: DateTimeOffset.UtcNow, withEquipment: true);

        await using (var db = await CharacterSeeding.OpenDbContextAsync(fixture))
        {
            var equipment = await db.CharacterEquipments.SingleAsync(e => e.CharacterId == character.Id);
            equipment.LastSyncedAt = DateTimeOffset.UtcNow.AddDays(-20);
            await db.SaveChangesAsync();
        }

        var due = await FindStaleAsync(DateTimeOffset.UtcNow.AddDays(-7), take: WholeStaleSet);

        Assert.Contains(due, c => c.Id == character.Id);
    }

    [Fact]
    public async Task FindStale_ReturnsTheOldestFirst()
    {
        // A compliance queue, not a work queue: the rows closest to breaching thirty days go first, so a
        // backlog becomes lateness on the newest rows rather than the oldest.
        var realm = await CharacterSeeding.CreateRealmAsync(fixture);
        var oldest = await SeedAsync(realm, syncedAt: DateTimeOffset.UtcNow.AddDays(-29));
        var middle = await SeedAsync(realm, syncedAt: DateTimeOffset.UtcNow.AddDays(-20));
        var newest = await SeedAsync(realm, syncedAt: DateTimeOffset.UtcNow.AddDays(-10));

        var due = await FindStaleAsync(DateTimeOffset.UtcNow.AddDays(-7), take: WholeStaleSet);

        var ours = due.Where(c => c.Id == oldest.Id || c.Id == middle.Id || c.Id == newest.Id).ToList();

        Assert.Equal([oldest.Id, middle.Id, newest.Id], ours.Select(c => c.Id));
    }

    [Fact]
    public async Task FindStale_NeverReturnsMoreThanTheRunsBudget()
    {
        // The per-run budget, enforced in SQL rather than by trimming afterwards — a batch trimmed in
        // memory would still have read every stale row in the table.
        var realm = await CharacterSeeding.CreateRealmAsync(fixture);

        for (var i = 0; i < 5; i++)
        {
            await SeedAsync(realm, syncedAt: DateTimeOffset.UtcNow.AddDays(-20));
        }

        Assert.Equal(3, (await FindStaleAsync(DateTimeOffset.UtcNow.AddDays(-7), take: 3)).Count);
    }

    private async Task<IReadOnlyList<StaleCharacterRef>> FindStaleAsync(DateTimeOffset staleBefore, int take)
    {
        await using var db = await CharacterSeeding.OpenDbContextAsync(fixture);
        return await new CharacterRepository(db).FindStaleAsync(staleBefore, take, CancellationToken.None);
    }

    // CharacterSeeding always stamps LastSyncedAt as "now", so these tests rewrite it afterwards rather
    // than widening the shared helper for one caller.
    private async Task<Domain.Managers.Models.Domain.Character> SeedAsync(
        Realm realm, DateTimeOffset syncedAt, bool withEquipment = true)
    {
        var character = await CharacterSeeding.CreateCharacterAsync(fixture, realm.Id, withEquipment: withEquipment);

        await using var db = await CharacterSeeding.OpenDbContextAsync(fixture);
        var stored = await db.Characters.SingleAsync(c => c.Id == character.Id);
        stored.LastSyncedAt = syncedAt;

        if (withEquipment)
        {
            var equipment = await db.CharacterEquipments.SingleAsync(e => e.CharacterId == character.Id);
            equipment.LastSyncedAt = syncedAt;
        }

        await db.SaveChangesAsync();

        return character;
    }
}
