using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;

namespace AegisScribe.Domain.Managers.Mappers;

public static class CharacterMappers
{
    public static CharacterDetailServiceModel ToServiceModel(this Character character) => new()
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
    };

    public static EquippedItemServiceModel ToServiceModel(this EquippedItem item) => new()
    {
        Slot = item.Slot,
        BlizzardItemId = item.BlizzardItemId,
        ItemName = item.ItemName,
        Quality = item.Quality,
        ItemLevel = item.ItemLevel,
        IconName = item.IconName,
    };
}
