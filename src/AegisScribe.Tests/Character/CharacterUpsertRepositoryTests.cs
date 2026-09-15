using AegisScribe.Domain.Data;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Tests.Auth;
using AegisScribe.Tests.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace AegisScribe.Tests.Character;

// 6.4's write half, against real SQL Server — because every interesting failure here is a database
// constraint, and an in-memory provider enforces none of them. The unique indexes on
// (RealmId, NameLower), BlizzardCharacterId, CharacterEquipment.CharacterId and
// (CharacterEquipmentId, Slot) are the reason these methods are shaped the way they are, so the tests
// have to be somewhere those indexes exist.
[Collection("AegisScribe API")]
public class CharacterUpsertRepositoryTests(AegisScribeAppFixture fixture)
{
    [Fact]
    public async Task UpsertCharacter_InsertsANeverSeenCharacterAgainstTheGivenRealm()
    {
        var realm = await CharacterSeeding.CreateRealmAsync(fixture);
        var name = CharacterSeeding.UniqueName();
        var blizzardId = Random.Shared.NextInt64(1, long.MaxValue);

        await using (var db = await CharacterSeeding.OpenDbContextAsync(fixture))
        {
            var repository = new CharacterRepository(db);
            await repository.ExecuteInTransactionAsync(async ct =>
                await repository.UpsertCharacterAsync(Fetched(name, blizzardId), realm.Id, ct),
                CancellationToken.None);
        }

        await using (var verify = await CharacterSeeding.OpenDbContextAsync(fixture))
        {
            var stored = await new CharacterRepository(verify)
                .FindByRealmAndNameAsync(realm.Region, realm.Slug, name, CancellationToken.None);

            Assert.NotNull(stored);
            Assert.Equal(realm.Id, stored!.RealmId);
            Assert.Equal(blizzardId, stored.BlizzardCharacterId);
            Assert.Equal(80, stored.Level);
        }
    }

    [Fact]
    public async Task UpsertCharacter_UpdatesTheExistingRowRatherThanInsertingASecond()
    {
        var realm = await CharacterSeeding.CreateRealmAsync(fixture);
        var existing = await CharacterSeeding.CreateCharacterAsync(fixture, realm.Id, level: 70, itemLevel: 500);

        await using (var db = await CharacterSeeding.OpenDbContextAsync(fixture))
        {
            var repository = new CharacterRepository(db);
            var fresh = Fetched(existing.Name, existing.BlizzardCharacterId);
            fresh.Level = 80;
            fresh.ItemLevel = 639;

            await repository.ExecuteInTransactionAsync(
                async ct => await repository.UpsertCharacterAsync(fresh, realm.Id, ct),
                CancellationToken.None);
        }

        await using (var verify = await CharacterSeeding.OpenDbContextAsync(fixture))
        {
            var rows = await verify.Characters
                .Where(c => c.RealmId == realm.Id && c.NameLower == existing.Name.ToLowerInvariant())
                .ToListAsync();

            var stored = Assert.Single(rows);
            Assert.Equal(existing.Id, stored.Id);
            Assert.Equal(80, stored.Level);
            Assert.Equal(639, stored.ItemLevel);
        }
    }

    [Fact]
    public async Task UpsertCharacter_FindsARenamedCharacterBySourceIdInsteadOfCollidingOnIt()
    {
        // The case the schema forces. BlizzardCharacterId carries a unique index, so a character that
        // has been renamed arrives under a name we have never seen with an id we already hold — and a
        // plain insert would violate the index instead of recording the rename.
        var realm = await CharacterSeeding.CreateRealmAsync(fixture);
        var existing = await CharacterSeeding.CreateCharacterAsync(fixture, realm.Id);
        var newName = CharacterSeeding.UniqueName();

        await using (var db = await CharacterSeeding.OpenDbContextAsync(fixture))
        {
            var repository = new CharacterRepository(db);
            await repository.ExecuteInTransactionAsync(
                async ct => await repository.UpsertCharacterAsync(
                    Fetched(newName, existing.BlizzardCharacterId), realm.Id, ct),
                CancellationToken.None);
        }

        await using (var verify = await CharacterSeeding.OpenDbContextAsync(fixture))
        {
            var rows = await verify.Characters
                .Where(c => c.BlizzardCharacterId == existing.BlizzardCharacterId)
                .ToListAsync();

            var stored = Assert.Single(rows);
            Assert.Equal(existing.Id, stored.Id);
            Assert.Equal(newName, stored.Name);
            Assert.Equal(newName.ToLowerInvariant(), stored.NameLower);
        }
    }

    [Fact]
    public async Task ReplaceEquipment_CreatesTheSnapshotWhenTheCharacterHasNoneYet()
    {
        var realm = await CharacterSeeding.CreateRealmAsync(fixture);
        var character = await CharacterSeeding.CreateCharacterAsync(fixture, realm.Id, withEquipment: false);

        await using (var db = await CharacterSeeding.OpenDbContextAsync(fixture))
        {
            var repository = new CharacterRepository(db);
            await repository.ExecuteInTransactionAsync(async ct =>
            {
                await repository.ReplaceEquipmentAsync(
                    character.Id,
                    Equipment((EquipmentSlot.Head, "Helm", 620), (EquipmentSlot.MainHand, "Sword", 630)),
                    ct);
                return 0;
            }, CancellationToken.None);
        }

        await using (var verify = await CharacterSeeding.OpenDbContextAsync(fixture))
        {
            var stored = await new CharacterRepository(verify)
                .FindByRealmAndNameAsync(realm.Region, realm.Slug, character.Name, CancellationToken.None);

            Assert.Equal(2, stored!.Equipment!.EquippedItems.Count);
        }
    }

    [Fact]
    public async Task ReplaceEquipment_ReconcilesSlotsInPlace_UpdatingAddingAndRemoving()
    {
        // Slot by slot rather than delete-then-insert: EquippedItem has a unique index on
        // (CharacterEquipmentId, Slot), and clearing the set then re-adding it inside one SaveChanges
        // would depend on EF ordering those two statements favourably.
        var realm = await CharacterSeeding.CreateRealmAsync(fixture);
        var character = await CharacterSeeding.CreateCharacterAsync(fixture, realm.Id, withEquipment: true);

        await using (var db = await CharacterSeeding.OpenDbContextAsync(fixture))
        {
            var repository = new CharacterRepository(db);
            await repository.ExecuteInTransactionAsync(async ct =>
            {
                // The seed equips MainHand only. This upgrades that slot, adds a Head, and leaves
                // nothing else behind.
                await repository.ReplaceEquipmentAsync(
                    character.Id,
                    Equipment((EquipmentSlot.MainHand, "Better Sword", 645), (EquipmentSlot.Head, "Helm", 620)),
                    ct);
                return 0;
            }, CancellationToken.None);
        }

        await using (var verify = await CharacterSeeding.OpenDbContextAsync(fixture))
        {
            var stored = await new CharacterRepository(verify)
                .FindByRealmAndNameAsync(realm.Region, realm.Slug, character.Name, CancellationToken.None);

            var items = stored!.Equipment!.EquippedItems.OrderBy(i => i.Slot).ToList();
            Assert.Equal(2, items.Count);

            var head = Assert.Single(items, i => i.Slot == EquipmentSlot.Head);
            Assert.Equal("Helm", head.ItemName);

            var mainHand = Assert.Single(items, i => i.Slot == EquipmentSlot.MainHand);
            Assert.Equal("Better Sword", mainHand.ItemName);
            Assert.Equal(645, mainHand.ItemLevel);
        }
    }

    [Fact]
    public async Task ReplaceEquipment_RemovesASlotTheCharacterHasUnequipped()
    {
        var realm = await CharacterSeeding.CreateRealmAsync(fixture);
        var character = await CharacterSeeding.CreateCharacterAsync(fixture, realm.Id, withEquipment: true);

        await using (var db = await CharacterSeeding.OpenDbContextAsync(fixture))
        {
            var repository = new CharacterRepository(db);
            await repository.ExecuteInTransactionAsync(async ct =>
            {
                await repository.ReplaceEquipmentAsync(character.Id, Equipment((EquipmentSlot.Head, "Helm", 620)), ct);
                return 0;
            }, CancellationToken.None);
        }

        await using (var verify = await CharacterSeeding.OpenDbContextAsync(fixture))
        {
            var stored = await new CharacterRepository(verify)
                .FindByRealmAndNameAsync(realm.Region, realm.Slug, character.Name, CancellationToken.None);

            // Leaving the old item there would show gear the character has taken off.
            var item = Assert.Single(stored!.Equipment!.EquippedItems);
            Assert.Equal(EquipmentSlot.Head, item.Slot);
        }
    }

    private static Domain.Managers.Models.Domain.Character Fetched(string name, long blizzardCharacterId) => new()
    {
        Name = name,
        NameLower = name.ToLowerInvariant(),
        Level = 80,
        Class = CharacterClass.Shaman,
        Spec = "Enhancement",
        ItemLevel = 620,
        Faction = CharacterFaction.Horde,
        BlizzardCharacterId = blizzardCharacterId,
        LastSyncedAt = DateTimeOffset.UtcNow,
    };

    private static CharacterEquipment Equipment(params (EquipmentSlot Slot, string Name, int ItemLevel)[] items) => new()
    {
        LastSyncedAt = DateTimeOffset.UtcNow,
        EquippedItems = [.. items.Select(item => new EquippedItem
        {
            Slot = item.Slot,
            BlizzardItemId = Random.Shared.NextInt64(1, int.MaxValue),
            ItemName = item.Name,
            Quality = ItemQuality.Epic,
            ItemLevel = item.ItemLevel,
        })],
    };
}
