using AegisScribe.Domain.Data;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Tests.Auth;
using AegisScribe.Tests.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace AegisScribe.Tests.Character;

// 6.4b — the realm repository, against real SQL Server. Every rule these methods encode is a database
// constraint: the unique index on (Region, Slug), and the filtered unique index on BlizzardRealmId that
// permits many rows at 0 but only one per real Blizzard id. Neither exists in an in-memory provider, so
// neither would be tested by one.
[Collection("AegisScribe API")]
public class RealmRepositoryTests(AegisScribeAppFixture fixture)
{
    [Fact]
    public async Task Find_ReturnsTheRealmForItsRegionAndSlugOnly()
    {
        var realm = await CharacterSeeding.CreateRealmAsync(fixture, region: "us");

        await using var db = await CharacterSeeding.OpenDbContextAsync(fixture);
        var repository = new RealmRepository(db);

        Assert.NotNull(await repository.FindAsync("us", realm.Slug, CancellationToken.None));
        Assert.Null(await repository.FindAsync("eu", realm.Slug, CancellationToken.None));
        Assert.Null(await repository.FindAsync("us", CharacterSeeding.UniqueSlug(), CancellationToken.None));
    }

    [Fact]
    public async Task Upsert_InsertsWhenAbsentAndUpdatesInPlaceWhenPresent()
    {
        var slug = CharacterSeeding.UniqueSlug();
        var blizzardRealmId = Random.Shared.NextInt64(1, long.MaxValue);

        await UpsertAsync(Fetched(slug, blizzardRealmId, "Emberfall", DateTimeOffset.UtcNow.AddDays(-10)));

        Guid insertedId;

        await using (var db = await CharacterSeeding.OpenDbContextAsync(fixture))
        {
            var inserted = await db.Realms.SingleAsync(r => r.Region == "us" && r.Slug == slug);
            insertedId = inserted.Id;
            Assert.Equal("Emberfall", inserted.Name);
            Assert.Equal(blizzardRealmId, inserted.BlizzardRealmId);
        }

        await UpsertAsync(Fetched(slug, blizzardRealmId, "Emberfall-Renamed", DateTimeOffset.UtcNow));

        await using (var db = await CharacterSeeding.OpenDbContextAsync(fixture))
        {
            var updated = await db.Realms.SingleAsync(r => r.Region == "us" && r.Slug == slug);
            Assert.Equal(insertedId, updated.Id);
            Assert.Equal("Emberfall-Renamed", updated.Name);
        }
    }

    [Fact]
    public async Task Upsert_MovesARenamedRealmBySourceIdRatherThanInsertingASecond()
    {
        // The reason BlizzardRealmId exists at all. Blizzard renames a realm, so the slug changes while
        // the id does not — and matching on the slug would leave every character on the old row.
        var blizzardRealmId = Random.Shared.NextInt64(1, long.MaxValue);
        var oldSlug = CharacterSeeding.UniqueSlug();
        var newSlug = CharacterSeeding.UniqueSlug();

        await UpsertAsync(Fetched(oldSlug, blizzardRealmId, "Old Name", DateTimeOffset.UtcNow.AddDays(-10)));

        Guid originalId;

        await using (var db = await CharacterSeeding.OpenDbContextAsync(fixture))
        {
            originalId = (await db.Realms.SingleAsync(r => r.BlizzardRealmId == blizzardRealmId)).Id;
        }

        await UpsertAsync(Fetched(newSlug, blizzardRealmId, "New Name", DateTimeOffset.UtcNow));

        await using (var verify = await CharacterSeeding.OpenDbContextAsync(fixture))
        {
            var rows = await verify.Realms.Where(r => r.BlizzardRealmId == blizzardRealmId).ToListAsync();

            var stored = Assert.Single(rows);
            Assert.Equal(originalId, stored.Id);
            Assert.Equal(newSlug, stored.Slug);
            Assert.Equal("New Name", stored.Name);
        }
    }

    [Fact]
    public async Task Upsert_AdoptsARowThatPredatesTheCatalogueInsteadOfCollidingOnItsSlug()
    {
        // CharacterSeeding writes realms with no BlizzardRealmId — exactly like a seeded demo realm, or
        // one 6.4 resolved lazily before the catalogue sync ever ran. Matching on source id alone would
        // miss it and then violate the unique index on (Region, Slug).
        var existing = await CharacterSeeding.CreateRealmAsync(fixture, region: "us");
        Assert.Equal(0, existing.BlizzardRealmId);

        var blizzardRealmId = Random.Shared.NextInt64(1, long.MaxValue);
        await UpsertAsync(Fetched(existing.Slug, blizzardRealmId, "Adopted", DateTimeOffset.UtcNow));

        await using var verify = await CharacterSeeding.OpenDbContextAsync(fixture);
        var rows = await verify.Realms.Where(r => r.Region == "us" && r.Slug == existing.Slug).ToListAsync();

        var stored = Assert.Single(rows);
        Assert.Equal(existing.Id, stored.Id);
        Assert.Equal(blizzardRealmId, stored.BlizzardRealmId);
    }

    [Fact]
    public async Task UpsertCatalogue_InsertsTheWholeSetAndIsIdempotentOnASecondPass()
    {
        // The prompt's idempotency assertion, corrected: a second pass must not duplicate a row, but it
        // MUST advance LastSyncedAt — that column is the 30-day compliance clock, not a change marker,
        // so a pass that confirmed a realm unchanged has still refreshed it.
        var region = $"t{Guid.NewGuid():N}"[..6];
        var first = DateTimeOffset.UtcNow.AddDays(-1);
        var catalogue = Catalogue(region, count: 5, syncedAt: first);

        var inserted = await UpsertCatalogueAsync(region, catalogue);
        Assert.Equal(5, inserted);

        List<Guid> firstIds;

        await using (var db = await CharacterSeeding.OpenDbContextAsync(fixture))
        {
            var rows = await db.Realms.Where(r => r.Region == region).OrderBy(r => r.Slug).ToListAsync();
            Assert.Equal(5, rows.Count);
            firstIds = [.. rows.Select(r => r.Id)];
        }

        var second = DateTimeOffset.UtcNow;
        var insertedAgain = await UpsertCatalogueAsync(region, Catalogue(region, count: 5, syncedAt: second));

        Assert.Equal(0, insertedAgain);

        await using (var verify = await CharacterSeeding.OpenDbContextAsync(fixture))
        {
            var rows = await verify.Realms.Where(r => r.Region == region).OrderBy(r => r.Slug).ToListAsync();

            Assert.Equal(5, rows.Count);
            Assert.Equal(firstIds, rows.Select(r => r.Id));
            Assert.All(rows, row => Assert.True(row.LastSyncedAt > first));
        }
    }

    [Fact]
    public async Task UpsertCatalogue_DoesNotDeleteARealmMissingFromThePass()
    {
        // A realm absent from Blizzard's answer is far likelier to be a partial pass than a realm that
        // ceased to exist, and deleting one would orphan every character on it.
        var region = $"t{Guid.NewGuid():N}"[..6];
        await UpsertCatalogueAsync(region, Catalogue(region, count: 3, syncedAt: DateTimeOffset.UtcNow));

        await UpsertCatalogueAsync(region, Catalogue(region, count: 1, syncedAt: DateTimeOffset.UtcNow));

        await using var verify = await CharacterSeeding.OpenDbContextAsync(fixture);
        Assert.Equal(3, await verify.Realms.CountAsync(r => r.Region == region));
    }

    [Fact]
    public async Task LatestSyncedAt_ReportsTheNewestRowInTheRegionAndNullWhenThereAreNone()
    {
        var region = $"t{Guid.NewGuid():N}"[..6];

        await using (var db = await CharacterSeeding.OpenDbContextAsync(fixture))
        {
            Assert.Null(await new RealmRepository(db).LatestSyncedAtAsync(region, CancellationToken.None));
        }

        var newest = DateTimeOffset.UtcNow;
        await UpsertAsync(Fetched(CharacterSeeding.UniqueSlug(), Random.Shared.NextInt64(1, long.MaxValue), "Old", newest.AddDays(-30), region));
        await UpsertAsync(Fetched(CharacterSeeding.UniqueSlug(), Random.Shared.NextInt64(1, long.MaxValue), "New", newest, region));

        await using (var db = await CharacterSeeding.OpenDbContextAsync(fixture))
        {
            var latest = await new RealmRepository(db).LatestSyncedAtAsync(region, CancellationToken.None);

            Assert.NotNull(latest);

            // SQL Server's datetimeoffset rounds to 100ns ticks, so this compares to the second rather
            // than for exact equality.
            Assert.True((latest!.Value - newest).Duration() < TimeSpan.FromSeconds(1));
        }
    }

    private async Task UpsertAsync(Realm realm)
    {
        await using var db = await CharacterSeeding.OpenDbContextAsync(fixture);
        var repository = new RealmRepository(db);
        await repository.ExecuteInTransactionAsync(ct => repository.UpsertAsync(realm, ct), CancellationToken.None);
    }

    private async Task<int> UpsertCatalogueAsync(string region, IReadOnlyList<Realm> fresh)
    {
        await using var db = await CharacterSeeding.OpenDbContextAsync(fixture);
        var repository = new RealmRepository(db);
        return await repository.ExecuteInTransactionAsync(
            ct => repository.UpsertCatalogueAsync(region, fresh, ct), CancellationToken.None);
    }

    // A stable per-region catalogue: the same slugs and source ids every time it is called, so a second
    // pass is genuinely the same data rather than a new set that would trivially insert.
    private static List<Realm> Catalogue(string region, int count, DateTimeOffset syncedAt) =>
        [.. Enumerable.Range(1, count).Select(i => new Realm
        {
            Region = region,
            Slug = $"{region}-realm-{i}",
            Name = $"Realm {i}",
            BlizzardRealmId = Math.Abs(HashCode.Combine(region, i)) + 1_000_000L,
            BlizzardConnectedRealmId = 5000 + (i % 2),
            LastSyncedAt = syncedAt,
        })];

    private static Realm Fetched(
        string slug, long blizzardRealmId, string name, DateTimeOffset syncedAt, string region = "us") => new()
    {
        Region = region,
        Slug = slug,
        Name = name,
        BlizzardRealmId = blizzardRealmId,
        BlizzardConnectedRealmId = 1092,
        LastSyncedAt = syncedAt,
    };
}
