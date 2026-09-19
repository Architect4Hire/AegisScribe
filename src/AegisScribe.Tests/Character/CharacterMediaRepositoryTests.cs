using AegisScribe.Domain.Data;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Tests.Auth;
using Microsoft.EntityFrameworkCore;

namespace AegisScribe.Tests.Character;

// Renders and icons against real SQL Server. The icon dedupe is a set of set-based UPDATEs and a
// DISTINCT over a shared table, which is exactly what an in-memory provider would let pass untested.
//
// The database is shared across the suite, so every assertion here is about rows this test created —
// item ids are random, and the selection queries are asked with a take large enough to see them all.
[Collection("AegisScribe API")]
public class CharacterMediaRepositoryTests(AegisScribeAppFixture fixture)
{
    private const int Everything = 1_000_000;

    private static readonly DateTimeOffset LongAgo = DateTimeOffset.UtcNow.AddDays(-365);

    [Fact]
    public async Task UpsertCharacter_StoresRendersWhenTheRefreshAskedForThem()
    {
        var realm = await CharacterSeeding.CreateRealmAsync(fixture);
        var existing = await CharacterSeeding.CreateCharacterAsync(fixture, realm.Id);

        var fresh = Fetched(existing);
        fresh.ApplyMedia(new CharacterMedia("https://render/avatar.jpg", "https://render/main-raw.png"), fresh.LastSyncedAt);
        await UpsertAsync(fresh, realm.Id);

        var stored = await ReadAsync(existing.Id);
        Assert.Equal("https://render/avatar.jpg", stored.AvatarUrl);
        Assert.Equal("https://render/main-raw.png", stored.RenderUrl);
        Assert.NotNull(stored.MediaSyncedAt);
    }

    [Fact]
    public async Task UpsertCharacter_KeepsStoredRendersWhenTheMediaCallGotNoAnswer()
    {
        // MediaSyncedAt stays null on the fresh entity when Blizzard could not be asked. Overwriting
        // would blank renders we hold on the strength of a request that never got an answer.
        var realm = await CharacterSeeding.CreateRealmAsync(fixture);
        var existing = await CharacterSeeding.CreateCharacterAsync(fixture, realm.Id);

        await using (var db = await CharacterSeeding.OpenDbContextAsync(fixture))
        {
            await new CharacterRepository(db).SetMediaAsync(
                existing.Id, new CharacterMedia("https://render/kept.jpg", "https://render/kept.png"), DateTimeOffset.UtcNow, CancellationToken.None);
        }

        await UpsertAsync(Fetched(existing), realm.Id);

        var stored = await ReadAsync(existing.Id);
        Assert.Equal("https://render/kept.jpg", stored.AvatarUrl);
        Assert.Equal("https://render/kept.png", stored.RenderUrl);
    }

    [Fact]
    public async Task FindMissingMedia_SelectsNeverAskedCharacters_AndSetMediaTakesThemOut()
    {
        var realm = await CharacterSeeding.CreateRealmAsync(fixture);
        var character = await CharacterSeeding.CreateCharacterAsync(fixture, realm.Id);

        await using var db = await CharacterSeeding.OpenDbContextAsync(fixture);
        var repository = new CharacterRepository(db);

        var before = await repository.FindMissingMediaAsync(LongAgo, Everything, CancellationToken.None);
        Assert.Contains(before, candidate => candidate.Id == character.Id);

        // "Asked, and there are none" counts as answered — a character with no renders is not re-asked
        // every pass.
        await repository.SetMediaAsync(character.Id, CharacterMedia.None, DateTimeOffset.UtcNow, CancellationToken.None);

        var after = await repository.FindMissingMediaAsync(LongAgo, Everything, CancellationToken.None);
        Assert.DoesNotContain(after, candidate => candidate.Id == character.Id);
    }

    [Fact]
    public async Task FindMissingMedia_PutsGuildMembersAheadOfOneOffLookups()
    {
        // A take of one, so the ordering decides who is chosen. The shared database holds many
        // never-asked characters from other tests; the guild member must still come first.
        var realm = await CharacterSeeding.CreateRealmAsync(fixture);
        var member = await CharacterSeeding.CreateCharacterAsync(fixture, realm.Id);

        await using var db = await CharacterSeeding.OpenDbContextAsync(fixture);
        var guild = new Guild
        {
            Id = Guid.NewGuid(),
            RealmId = realm.Id,
            Name = $"Test Guild {Guid.NewGuid():N}",
            NameLower = $"test guild {Guid.NewGuid():N}",
            BlizzardGuildId = Random.Shared.NextInt64(1, long.MaxValue),
            LastSyncedAt = DateTimeOffset.UtcNow,
        };
        db.Guilds.Add(guild);
        db.GuildMembers.Add(new GuildMember { Id = Guid.NewGuid(), GuildId = guild.Id, CharacterId = member.Id });
        await db.SaveChangesAsync();

        // Every other guild member in the shared database is also a candidate, so assert on the class
        // of the first pick rather than its identity.
        var first = Assert.Single(await new CharacterRepository(db).FindMissingMediaAsync(LongAgo, 1, CancellationToken.None));
        Assert.True(await db.GuildMembers.AnyAsync(m => m.CharacterId == first.Id));
    }

    [Fact]
    public async Task FindItemIdsNeedingIcon_ReturnsEachItemOnce_AndSkipsResolvedAndDemoRows()
    {
        var shared = NewItemId();
        var resolved = NewItemId();
        var demo = NewItemId();

        // Two characters wearing the same item: one Blizzard call must cover both.
        await EquipAsync((EquipmentSlot.Head, shared, null, null), (EquipmentSlot.Neck, resolved, "135349", DateTimeOffset.UtcNow));
        await EquipAsync((EquipmentSlot.Head, shared, null, null), (EquipmentSlot.Neck, demo, "inv_helm_04", null));

        await using var db = await CharacterSeeding.OpenDbContextAsync(fixture);
        var ids = await new CharacterRepository(db).FindItemIdsNeedingIconAsync(LongAgo, Everything, CancellationToken.None);

        Assert.Single(ids, id => id == shared);
        Assert.DoesNotContain(resolved, ids);
        // Named but never synced is seeded demo data. Blizzard has never heard of its id.
        Assert.DoesNotContain(demo, ids);
    }

    [Fact]
    public async Task SetItemIcon_WritesEveryRowWearingTheItem_ButNotDemoRows()
    {
        var itemId = NewItemId();

        var first = await EquipAsync((EquipmentSlot.Head, itemId, null, null));
        var second = await EquipAsync((EquipmentSlot.Head, itemId, null, null));
        var demo = await EquipAsync((EquipmentSlot.Head, itemId, "inv_demo_01", null));

        await using (var db = await CharacterSeeding.OpenDbContextAsync(fixture))
        {
            await new CharacterRepository(db).SetItemIconAsync(itemId, "135349", DateTimeOffset.UtcNow, CancellationToken.None);
        }

        Assert.Equal("135349", (await HeadOfAsync(first)).IconName);
        Assert.Equal("135349", (await HeadOfAsync(second)).IconName);
        Assert.Equal("inv_demo_01", (await HeadOfAsync(demo)).IconName);
    }

    [Fact]
    public async Task SetItemIcon_RecordsA404AsAnsweredSoTheItemIsNotAskedAgain()
    {
        var itemId = NewItemId();
        await EquipAsync((EquipmentSlot.Head, itemId, null, null));

        await using var db = await CharacterSeeding.OpenDbContextAsync(fixture);
        var repository = new CharacterRepository(db);

        await repository.SetItemIconAsync(itemId, null, DateTimeOffset.UtcNow, CancellationToken.None);

        var ids = await repository.FindItemIdsNeedingIconAsync(LongAgo, Everything, CancellationToken.None);
        Assert.DoesNotContain(itemId, ids);
    }

    [Fact]
    public async Task ReplaceEquipment_KeepsTheIconOfAnItemThatDidNotChange()
    {
        // The bug this guards: the equipment endpoint carries no icons, so a naive in-place update wrote
        // null over every icon the worker had filled in, on every refresh.
        var itemId = NewItemId();
        var characterId = await EquipAsync((EquipmentSlot.Head, itemId, "135349", DateTimeOffset.UtcNow));

        await ReplaceAsync(characterId, (EquipmentSlot.Head, itemId));

        var head = await HeadOfAsync(characterId);
        Assert.Equal("135349", head.IconName);
        Assert.NotNull(head.IconSyncedAt);
    }

    [Fact]
    public async Task ReplaceEquipment_CopiesAnIconAlreadyKnownFromAnotherCharacter()
    {
        var itemId = NewItemId();
        await EquipAsync((EquipmentSlot.Head, itemId, "135349", DateTimeOffset.UtcNow));

        var newcomer = await EquipAsync();
        await ReplaceAsync(newcomer, (EquipmentSlot.Head, itemId));

        Assert.Equal("135349", (await HeadOfAsync(newcomer)).IconName);
    }

    [Fact]
    public async Task ReplaceEquipment_LeavesAGenuinelyNewItemForTheWorker()
    {
        var newcomer = await EquipAsync();
        await ReplaceAsync(newcomer, (EquipmentSlot.Head, NewItemId()));

        var head = await HeadOfAsync(newcomer);
        Assert.Null(head.IconName);
        Assert.Null(head.IconSyncedAt);
    }

    private static long NewItemId() => Random.Shared.NextInt64(1_000_000_000, long.MaxValue);

    private static Domain.Managers.Models.Domain.Character Fetched(Domain.Managers.Models.Domain.Character existing) => new()
    {
        Name = existing.Name,
        NameLower = existing.NameLower,
        Level = existing.Level,
        Class = existing.Class,
        Spec = existing.Spec,
        ItemLevel = existing.ItemLevel,
        Faction = existing.Faction,
        BlizzardCharacterId = existing.BlizzardCharacterId,
        LastSyncedAt = DateTimeOffset.UtcNow,
    };

    private async Task UpsertAsync(Domain.Managers.Models.Domain.Character fresh, Guid realmId)
    {
        await using var db = await CharacterSeeding.OpenDbContextAsync(fixture);
        var repository = new CharacterRepository(db);

        await repository.ExecuteInTransactionAsync(
            async ct => await repository.UpsertCharacterAsync(fresh, realmId, ct),
            CancellationToken.None);
    }

    private async Task<Domain.Managers.Models.Domain.Character> ReadAsync(Guid characterId)
    {
        await using var db = await CharacterSeeding.OpenDbContextAsync(fixture);

        return await db.Characters.AsNoTracking().SingleAsync(c => c.Id == characterId);
    }

    // A new character with an equipment snapshot written straight to the table, so each row's icon state
    // is exactly what the test says rather than what ReplaceEquipmentAsync would make of it.
    private async Task<Guid> EquipAsync(
        params (EquipmentSlot Slot, long ItemId, string? IconName, DateTimeOffset? IconSyncedAt)[] items)
    {
        var realm = await CharacterSeeding.CreateRealmAsync(fixture);
        var character = await CharacterSeeding.CreateCharacterAsync(fixture, realm.Id, withEquipment: false);

        await using var db = await CharacterSeeding.OpenDbContextAsync(fixture);
        db.CharacterEquipments.Add(new CharacterEquipment
        {
            Id = Guid.NewGuid(),
            CharacterId = character.Id,
            LastSyncedAt = DateTimeOffset.UtcNow,
            EquippedItems = [.. items.Select(item => new EquippedItem
            {
                Id = Guid.NewGuid(),
                Slot = item.Slot,
                BlizzardItemId = item.ItemId,
                ItemName = "Test item",
                Quality = ItemQuality.Epic,
                ItemLevel = 620,
                IconName = item.IconName,
                IconSyncedAt = item.IconSyncedAt,
            })],
        });
        await db.SaveChangesAsync();

        return character.Id;
    }

    // What a refresh does: a snapshot straight from the equipment endpoint, so no icons at all.
    private async Task ReplaceAsync(Guid characterId, params (EquipmentSlot Slot, long ItemId)[] items)
    {
        await using var db = await CharacterSeeding.OpenDbContextAsync(fixture);
        var repository = new CharacterRepository(db);

        await repository.ExecuteInTransactionAsync(async ct =>
        {
            await repository.ReplaceEquipmentAsync(
                characterId,
                new CharacterEquipment
                {
                    LastSyncedAt = DateTimeOffset.UtcNow,
                    EquippedItems = [.. items.Select(item => new EquippedItem
                    {
                        Slot = item.Slot,
                        BlizzardItemId = item.ItemId,
                        ItemName = "Test item",
                        Quality = ItemQuality.Epic,
                        ItemLevel = 625,
                    })],
                },
                ct);
            return 0;
        }, CancellationToken.None);
    }

    private async Task<EquippedItem> HeadOfAsync(Guid characterId)
    {
        await using var db = await CharacterSeeding.OpenDbContextAsync(fixture);

        return await db.EquippedItems
            .AsNoTracking()
            .SingleAsync(i => i.CharacterEquipment.CharacterId == characterId && i.Slot == EquipmentSlot.Head);
    }
}
