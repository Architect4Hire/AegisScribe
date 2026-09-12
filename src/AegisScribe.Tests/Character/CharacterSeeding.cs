using AegisScribe.Domain.Context;
using AegisScribe.Domain.Data;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Tests.Auth;
using Aspire.Hosting.Testing;
using Microsoft.EntityFrameworkCore;

namespace AegisScribe.Tests.Character;

// Character/Realm are global reference data (tenancy.md) — no repository write method exists for
// them yet (upserts land with the sync worker), so tests reach the database directly, the same way
// TenantSeeding does for Tenant/TenantMembership.
internal static class CharacterSeeding
{
    public static string UniqueSlug() => $"realm-{Guid.NewGuid():N}";

    public static string UniqueName() => $"Char{Guid.NewGuid():N}"[..20];

    public static async Task<Realm> CreateRealmAsync(
        AegisScribeAppFixture fixture, string? region = null, string? slug = null)
    {
        await using var db = await OpenDbContextAsync(fixture);
        var realm = new Realm
        {
            Id = Guid.NewGuid(),
            Region = region ?? "us",
            Slug = slug ?? UniqueSlug(),
            Name = "Test Realm",
            BlizzardConnectedRealmId = Random.Shared.NextInt64(1, long.MaxValue),
            LastSyncedAt = DateTimeOffset.UtcNow,
        };
        db.Realms.Add(realm);
        await db.SaveChangesAsync();
        return realm;
    }

    public static async Task<Domain.Managers.Models.Domain.Character> CreateCharacterAsync(
        AegisScribeAppFixture fixture,
        Guid realmId,
        string? name = null,
        int level = 80,
        int itemLevel = 600,
        bool withEquipment = false)
    {
        await using var db = await OpenDbContextAsync(fixture);
        var characterName = name ?? UniqueName();
        var character = new Domain.Managers.Models.Domain.Character
        {
            Id = Guid.NewGuid(),
            RealmId = realmId,
            Name = characterName,
            NameLower = characterName.ToLowerInvariant(),
            Level = level,
            Class = CharacterClass.Warrior,
            Spec = "Protection",
            ItemLevel = itemLevel,
            Faction = CharacterFaction.Alliance,
            BlizzardCharacterId = Random.Shared.NextInt64(1, long.MaxValue),
            LastSyncedAt = DateTimeOffset.UtcNow,
        };
        db.Characters.Add(character);
        await db.SaveChangesAsync();

        if (withEquipment)
        {
            var equipment = new CharacterEquipment
            {
                Id = Guid.NewGuid(),
                CharacterId = character.Id,
                LastSyncedAt = DateTimeOffset.UtcNow,
            };
            db.CharacterEquipments.Add(equipment);
            db.EquippedItems.Add(new EquippedItem
            {
                Id = Guid.NewGuid(),
                CharacterEquipmentId = equipment.Id,
                Slot = EquipmentSlot.MainHand,
                BlizzardItemId = 12345,
                ItemName = "Test Sword",
                Quality = ItemQuality.Epic,
                ItemLevel = itemLevel,
            });
            await db.SaveChangesAsync();
        }

        return character;
    }

    public static async Task<AegisScribeDbContext> OpenDbContextAsync(AegisScribeAppFixture fixture)
    {
        var connectionString = await fixture.App.GetConnectionStringAsync("aegisscribedb");
        var options = new DbContextOptionsBuilder<AegisScribeDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        // Character/Realm carry no TenantId and no query filter (tenancy.md) — an always-unresolved
        // TenantContext is correct here, same reasoning as TenantSeeding.
        return new AegisScribeDbContext(options, new TenantContext());
    }
}
