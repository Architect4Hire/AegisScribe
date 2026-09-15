using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;

namespace AegisScribe.Domain.Managers.Mappers;

public static class CharacterMappers
{
    // Blizzard's media CDN, never Wowhead's zamimg.com. 56px is the smallest raster size Blizzard
    // exposes for an item icon — see .claude/skills/add-external-sync/references/blizzard-endpoints.md
    // -> "Images and media".
    private const string ItemIconBaseUrl = "https://render.worldofwarcraft.com/icons/56";

    // isDegraded comes from the DataLayer's CharacterReadResult, never from anything on the entity —
    // "this row is stale AND we could not refresh it" is not a fact LastSyncedAt can express.
    public static CharacterDetailServiceModel ToServiceModel(this Character character, bool isDegraded = false) => new()
    {
        Id = character.Id,
        RealmSlug = character.Realm.Slug,
        Name = character.Name,
        Level = character.Level,
        Class = character.Class,
        Spec = character.Spec,
        ItemLevel = character.ItemLevel,
        Faction = character.Faction,
        LastSyncedAt = character.LastSyncedAt,
        Equipment = character.Equipment?.EquippedItems
            .OrderBy(i => i.Slot)
            .Select(i => i.ToServiceModel())
            .ToList()
            ?? [],
        ClassColor = ClassColorHex(character.Class),
        IsDegraded = isDegraded,
    };

    // Exact hex match to _tokens.scss's --c-* tokens. One mapping, so the frontend never re-derives it
    // and a new class is added in one place — which is also why this is internal rather than private:
    // the roster's list projection needs the same value.
    internal static string ClassColorHex(CharacterClass characterClass) => characterClass switch
    {
        CharacterClass.DeathKnight => "#C41E3A",
        CharacterClass.DemonHunter => "#A330C9",
        CharacterClass.Druid => "#FF7C0A",
        CharacterClass.Evoker => "#33937F",
        CharacterClass.Hunter => "#AAD372",
        CharacterClass.Mage => "#3FC7EB",
        CharacterClass.Monk => "#00FF98",
        CharacterClass.Paladin => "#F48CBA",
        CharacterClass.Priest => "#FFFFFF",
        CharacterClass.Rogue => "#FFF468",
        CharacterClass.Shaman => "#0070DD",
        CharacterClass.Warlock => "#8788EE",
        CharacterClass.Warrior => "#C69B6D",
        _ => throw new ArgumentOutOfRangeException(nameof(characterClass), characterClass, null),
    };

    public static EquippedItemServiceModel ToServiceModel(this EquippedItem item) => new()
    {
        Slot = item.Slot,
        BlizzardItemId = item.BlizzardItemId,
        ItemName = item.ItemName,
        Quality = item.Quality,
        ItemLevel = item.ItemLevel,
        IconUrl = item.IconName is { } iconName ? $"{ItemIconBaseUrl}/{iconName}.jpg" : null,
    };
}
