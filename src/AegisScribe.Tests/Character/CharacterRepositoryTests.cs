using AegisScribe.Domain.Data;
using AegisScribe.Tests.Auth;
using AegisScribe.Tests.Tenancy;

namespace AegisScribe.Tests.Character;

// Reuses the Tenancy collection's AppHost instance (Tenancy/TenantRepositoryTests does the same) —
// this is pure database work with no HTTP calls, so it adds nothing to any collection's anonymous
// rate-limit budget and doesn't need its own AppHost boot.
[Collection("AegisScribe API")]
public class CharacterRepositoryTests(AegisScribeAppFixture fixture)
{
    [Fact]
    public async Task FindByRealmAndName_ReturnsTheCharacterWithRealmAndEquipment_AndNullWhenNotFound()
    {
        var realm = await CharacterSeeding.CreateRealmAsync(fixture);
        var character = await CharacterSeeding.CreateCharacterAsync(fixture, realm.Id, withEquipment: true);

        await using var db = await CharacterSeeding.OpenDbContextAsync(fixture);
        var repository = new CharacterRepository(db);

        var found = await repository.FindByRealmAndNameAsync(realm.Region, realm.Slug, character.Name, CancellationToken.None);
        var wrongRegion = await repository.FindByRealmAndNameAsync("eu", realm.Slug, character.Name, CancellationToken.None);
        var unknownName = await repository.FindByRealmAndNameAsync(realm.Region, realm.Slug, CharacterSeeding.UniqueName(), CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(character.Id, found!.Id);
        Assert.Equal(realm.Slug, found.Realm.Slug);
        Assert.NotNull(found.Equipment);
        Assert.Single(found.Equipment!.EquippedItems);

        Assert.Null(wrongRegion);
        Assert.Null(unknownName);
    }

    [Fact]
    public async Task FindByRealmAndName_IsCaseInsensitiveOnName()
    {
        var realm = await CharacterSeeding.CreateRealmAsync(fixture);
        var character = await CharacterSeeding.CreateCharacterAsync(fixture, realm.Id);

        await using var db = await CharacterSeeding.OpenDbContextAsync(fixture);
        var repository = new CharacterRepository(db);

        var found = await repository.FindByRealmAndNameAsync(realm.Region, realm.Slug, character.Name.ToUpperInvariant(), CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(character.Id, found!.Id);
    }

    [Fact]
    public async Task Search_OrdersByNameThenId_AndRespectsNameFilterAndTake()
    {
        var realm = await CharacterSeeding.CreateRealmAsync(fixture);
        var prefix = $"Zz{Guid.NewGuid():N}"[..12];
        var first = await CharacterSeeding.CreateCharacterAsync(fixture, realm.Id, name: $"{prefix}Alpha");
        var second = await CharacterSeeding.CreateCharacterAsync(fixture, realm.Id, name: $"{prefix}Bravo");
        var third = await CharacterSeeding.CreateCharacterAsync(fixture, realm.Id, name: $"{prefix}Charlie");
        await CharacterSeeding.CreateCharacterAsync(fixture, realm.Id); // unrelated name — must not match the prefix filter

        await using var db = await CharacterSeeding.OpenDbContextAsync(fixture);
        var repository = new CharacterRepository(db);

        var page = await repository.SearchAsync(realm.Region, null, prefix.ToLowerInvariant(), null, null, take: 2, CancellationToken.None);

        Assert.Equal(2, page.Count);
        Assert.Equal(first.Id, page[0].Id);
        Assert.Equal(second.Id, page[1].Id);
        Assert.Equal(realm.Slug, page[0].RealmSlug);

        var nextPage = await repository.SearchAsync(
            realm.Region, null, prefix.ToLowerInvariant(), page[^1].Name.ToLowerInvariant(), page[^1].Id, take: 10, CancellationToken.None);

        Assert.Single(nextPage);
        Assert.Equal(third.Id, nextPage[0].Id);
    }

    [Fact]
    public async Task Search_FiltersByRegionAndRealmSlug()
    {
        var realmA = await CharacterSeeding.CreateRealmAsync(fixture, region: "us");
        var realmB = await CharacterSeeding.CreateRealmAsync(fixture, region: "eu");
        var prefix = $"Zz{Guid.NewGuid():N}"[..12];
        var inScope = await CharacterSeeding.CreateCharacterAsync(fixture, realmA.Id, name: $"{prefix}One");
        await CharacterSeeding.CreateCharacterAsync(fixture, realmB.Id, name: $"{prefix}Two"); // different region, same prefix

        await using var db = await CharacterSeeding.OpenDbContextAsync(fixture);
        var repository = new CharacterRepository(db);

        var byRegion = await repository.SearchAsync("us", null, prefix.ToLowerInvariant(), null, null, take: 10, CancellationToken.None);
        Assert.Single(byRegion);
        Assert.Equal(inScope.Id, byRegion[0].Id);

        var byRealm = await repository.SearchAsync(realmA.Region, realmA.Slug, prefix.ToLowerInvariant(), null, null, take: 10, CancellationToken.None);
        Assert.Single(byRealm);
        Assert.Equal(inScope.Id, byRealm[0].Id);
    }

    [Fact]
    public async Task ExecuteInTransaction_CommitsOnSuccess_AndRollsBackOnException()
    {
        var realm = await CharacterSeeding.CreateRealmAsync(fixture);

        await using var commitDb = await CharacterSeeding.OpenDbContextAsync(fixture);
        var commitRepository = new CharacterRepository(commitDb);
        var committedName = CharacterSeeding.UniqueName();

        await commitRepository.ExecuteInTransactionAsync(_ =>
        {
            commitDb.Characters.Add(new Domain.Managers.Models.Domain.Character
            {
                Id = Guid.NewGuid(),
                RealmId = realm.Id,
                Name = committedName,
                NameLower = committedName.ToLowerInvariant(),
                Level = 80,
                Class = Domain.Managers.Models.Domain.CharacterClass.Mage,
                ItemLevel = 600,
                Faction = Domain.Managers.Models.Domain.CharacterFaction.Horde,
                BlizzardCharacterId = Random.Shared.NextInt64(1, long.MaxValue),
                LastSyncedAt = DateTimeOffset.UtcNow,
            });
            return Task.FromResult(true);
        }, CancellationToken.None);

        await using var verifyDb = await CharacterSeeding.OpenDbContextAsync(fixture);
        Assert.NotNull(await new CharacterRepository(verifyDb)
            .FindByRealmAndNameAsync(realm.Region, realm.Slug, committedName, CancellationToken.None));

        await using var rollbackDb = await CharacterSeeding.OpenDbContextAsync(fixture);
        var rollbackRepository = new CharacterRepository(rollbackDb);
        var rolledBackName = CharacterSeeding.UniqueName();

        await Assert.ThrowsAsync<InvalidOperationException>(() => rollbackRepository.ExecuteInTransactionAsync<bool>(_ =>
        {
            rollbackDb.Characters.Add(new Domain.Managers.Models.Domain.Character
            {
                Id = Guid.NewGuid(),
                RealmId = realm.Id,
                Name = rolledBackName,
                NameLower = rolledBackName.ToLowerInvariant(),
                Level = 80,
                Class = Domain.Managers.Models.Domain.CharacterClass.Mage,
                ItemLevel = 600,
                Faction = Domain.Managers.Models.Domain.CharacterFaction.Horde,
                BlizzardCharacterId = Random.Shared.NextInt64(1, long.MaxValue),
                LastSyncedAt = DateTimeOffset.UtcNow,
            });
            throw new InvalidOperationException("simulated failure");
        }, CancellationToken.None));

        await using var verifyRollbackDb = await CharacterSeeding.OpenDbContextAsync(fixture);
        Assert.Null(await new CharacterRepository(verifyRollbackDb)
            .FindByRealmAndNameAsync(realm.Region, realm.Slug, rolledBackName, CancellationToken.None));
    }
}
