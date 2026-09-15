using AegisScribe.Domain.Data;
using AegisScribe.Domain.Integration.Blizzard;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Tests.Auth;
using AegisScribe.Tests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace AegisScribe.Tests.Character;

// A never-synced character arriving from Blizzard, with everything real except Blizzard itself — only
// IBlizzardGateway is stubbed. CharacterDataLayerTests pins the sequencing against substitutes; this
// pins that it actually lands rows in a database, which is the part a substitute cannot tell you.
[Collection("AegisScribe API")]
public class CharacterCacheFirstReadTests(AegisScribeAppFixture fixture)
{
    private const string Region = "us";

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-14T12:00:00Z");

    [Fact]
    public async Task ANeverSyncedCharacterArrivesFromBlizzardAndIsServedLocallyAfterwards()
    {
        // A realm slug and a character this database has never seen. Nothing is seeded on purpose.
        var realmSlug = CharacterSeeding.UniqueSlug();
        var name = CharacterSeeding.UniqueName();
        var blizzardCharacterId = Random.Shared.NextInt64(1, long.MaxValue);

        var gateway = StubGateway(realmSlug, name, blizzardCharacterId);

        CharacterReadResult firstRead;

        await using (var db = await CharacterSeeding.OpenDbContextAsync(fixture))
        {
            firstRead = await CreateDataLayer(db, gateway)
                .GetCharacterAsync(Region, realmSlug, name, CancellationToken.None);
        }

        // 1. It came back, and it is not degraded — this is live data, not a stale row being apologised
        //    for.
        Assert.NotNull(firstRead.Character);
        Assert.False(firstRead.IsDegraded);
        Assert.Equal(name, firstRead.Character!.Name);
        Assert.Equal(CharacterClass.Shaman, firstRead.Character.Class);
        Assert.Equal(639, firstRead.Character.ItemLevel);
        Assert.Equal(Now, firstRead.Character.LastSyncedAt);

        // 2. The realm it hangs from was resolved and stored too, connected-realm id and all.
        Assert.Equal(realmSlug, firstRead.Character.Realm.Slug);
        Assert.Equal(1092L, firstRead.Character.Realm.BlizzardConnectedRealmId);

        // 3. The gear came across on its own endpoint and is attached.
        Assert.Equal(2, firstRead.Character.Equipment!.EquippedItems.Count);

        // 4. Blizzard was asked exactly once for each of the three documents.
        await gateway.Received(1).FetchRealmAsync(Region, realmSlug, Arg.Any<CancellationToken>());
        await gateway.Received(1).FetchCharacterAsync(realmSlug, name, Arg.Any<CancellationToken>());
        await gateway.Received(1).FetchEquipmentAsync(realmSlug, name, Arg.Any<CancellationToken>());

        // 5. The rows are genuinely in SQL, not just in the answer that came back.
        await using (var verify = await CharacterSeeding.OpenDbContextAsync(fixture))
        {
            var stored = await verify.Characters
                .Include(c => c.Realm)
                .Include(c => c.Equipment!)
                    .ThenInclude(e => e.EquippedItems)
                .SingleAsync(c => c.BlizzardCharacterId == blizzardCharacterId);

            Assert.Equal(name.ToLowerInvariant(), stored.NameLower);
            Assert.Equal(realmSlug, stored.Realm.Slug);
            Assert.Equal(2, stored.Equipment!.EquippedItems.Count);
            Assert.Contains(stored.Equipment.EquippedItems, i => i.Slot == EquipmentSlot.Head);
            Assert.Contains(stored.Equipment.EquippedItems, i => i.Slot == EquipmentSlot.MainHand);
        }

        // 6. The second read is the one that matters for the rate limit: served entirely from SQL, with
        //    no further Blizzard calls at all. 36,000 calls an hour is contractual, and a read path that
        //    called out every time would spend it on data already stored.
        await using (var db = await CharacterSeeding.OpenDbContextAsync(fixture))
        {
            var secondRead = await CreateDataLayer(db, gateway)
                .GetCharacterAsync(Region, realmSlug, name, CancellationToken.None);

            Assert.NotNull(secondRead.Character);
            Assert.False(secondRead.IsDegraded);
            Assert.Equal(2, secondRead.Character!.Equipment!.EquippedItems.Count);
        }

        await gateway.Received(1).FetchCharacterAsync(realmSlug, name, Arg.Any<CancellationToken>());
        await gateway.Received(1).FetchEquipmentAsync(realmSlug, name, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AStoredCharacterPastTheRefreshWindowIsUpdatedInPlaceFromBlizzard()
    {
        var realm = await CharacterSeeding.CreateRealmAsync(fixture, region: Region);
        var character = await CharacterSeeding.CreateCharacterAsync(
            fixture, realm.Id, level: 70, itemLevel: 500, withEquipment: true);

        var gateway = StubGateway(realm.Slug, character.Name, character.BlizzardCharacterId);

        // The stored rows were written "now" by the seeder; this clock is a year later, so they are well
        // past the seven-day window.
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow.AddYears(1));

        CharacterReadResult result;

        await using (var db = await CharacterSeeding.OpenDbContextAsync(fixture))
        {
            result = await CreateDataLayer(db, gateway, time)
                .GetCharacterAsync(Region, realm.Slug, character.Name, CancellationToken.None);
        }

        Assert.False(result.IsDegraded);
        Assert.Equal(639, result.Character!.ItemLevel);

        // The realm was already held, so it cost no call — resolving one we have would be pure waste.
        await gateway.DidNotReceiveWithAnyArgs().FetchRealmAsync(default!, default!, default);

        await using (var verify = await CharacterSeeding.OpenDbContextAsync(fixture))
        {
            // Updated in place. A second row for the same character would be a unique-index violation on
            // (RealmId, NameLower) anyway, which is what makes this worth asserting rather than assuming.
            var rows = await verify.Characters
                .Where(c => c.BlizzardCharacterId == character.BlizzardCharacterId)
                .ToListAsync();

            var stored = Assert.Single(rows);
            Assert.Equal(character.Id, stored.Id);
            Assert.Equal(80, stored.Level);
        }
    }

    [Fact]
    public async Task WhenBlizzardIsUnreachableTheStoredRowIsServedAndFlaggedDegraded()
    {
        var realm = await CharacterSeeding.CreateRealmAsync(fixture, region: Region);
        var character = await CharacterSeeding.CreateCharacterAsync(fixture, realm.Id, withEquipment: true);

        var gateway = Substitute.For<IBlizzardGateway>();
        gateway.IsConfigured.Returns(true);
        gateway.FetchCharacterAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<Domain.Managers.Models.Domain.Character?>>(
                _ => throw new BlizzardUnavailableException("Blizzard could not be reached."));

        var time = new FakeTimeProvider(DateTimeOffset.UtcNow.AddYears(1));

        await using var db = await CharacterSeeding.OpenDbContextAsync(fixture);
        var result = await CreateDataLayer(db, gateway, time)
            .GetCharacterAsync(Region, realm.Slug, character.Name, CancellationToken.None);

        // The page still renders. It just says so.
        Assert.NotNull(result.Character);
        Assert.True(result.IsDegraded);
        Assert.Equal(character.Id, result.Character!.Id);
    }

    private static CharacterDataLayer CreateDataLayer(
        AegisScribeDbContext db,
        IBlizzardGateway gateway,
        TimeProvider? timeProvider = null)
    {
        var staleness = new BlizzardStalenessPolicy(
            new OptionsWrapper<BlizzardStalenessOptions>(new BlizzardStalenessOptions()),
            timeProvider ?? new FakeTimeProvider(Now));

        return new CharacterDataLayer(
            new CharacterRepository(db),
            new RealmRepository(db),
            gateway,
            staleness,
            NullLogger<CharacterDataLayer>.Instance);
    }

    // Stands in for Blizzard answering all three documents. Everything it returns is shaped the way
    // BlizzardGateway's mappers shape it — no RealmId on the character, no CharacterId on the equipment,
    // because the gateway has no store and will not invent either.
    private static IBlizzardGateway StubGateway(string realmSlug, string name, long blizzardCharacterId)
    {
        var gateway = Substitute.For<IBlizzardGateway>();
        gateway.IsConfigured.Returns(true);

        gateway.FetchRealmAsync(Region, realmSlug, Arg.Any<CancellationToken>()).Returns(new Realm
        {
            Region = Region,
            Slug = realmSlug,
            Name = "Argent Dawn",
            BlizzardConnectedRealmId = 1092,
            LastSyncedAt = Now,
        });

        gateway.FetchCharacterAsync(realmSlug, name, Arg.Any<CancellationToken>())
            .Returns(new Domain.Managers.Models.Domain.Character
            {
                Name = name,
                NameLower = name.ToLowerInvariant(),
                Level = 80,
                Class = CharacterClass.Shaman,
                Spec = "Enhancement",
                ItemLevel = 639,
                Faction = CharacterFaction.Horde,
                BlizzardCharacterId = blizzardCharacterId,
                LastSyncedAt = Now,
            });

        gateway.FetchEquipmentAsync(realmSlug, name, Arg.Any<CancellationToken>())
            .Returns(new CharacterEquipment
            {
                LastSyncedAt = Now,
                EquippedItems =
                [
                    new EquippedItem
                    {
                        Slot = EquipmentSlot.Head,
                        BlizzardItemId = 212010,
                        ItemName = "Cyclopean Cage Helm",
                        Quality = ItemQuality.Epic,
                        ItemLevel = 639,
                    },
                    new EquippedItem
                    {
                        Slot = EquipmentSlot.MainHand,
                        BlizzardItemId = 222440,
                        ItemName = "Charged Slicer of the Deep",
                        Quality = ItemQuality.Epic,
                        ItemLevel = 639,
                    },
                ],
            });

        return gateway;
    }
}
