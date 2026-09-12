namespace AegisScribe.Domain.Managers.Models.Domain;

public class Character
{
    public Guid Id { get; set; }
    public Guid RealmId { get; set; }
    public Realm Realm { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string NameLower { get; set; } = null!;
    public int Level { get; set; }
    public CharacterClass Class { get; set; }
    public string? Spec { get; set; }
    public int ItemLevel { get; set; }
    public CharacterFaction Faction { get; set; }
    // Never optional on a synced global entity (backend.md) — the erasure routine targets rows by
    // this id, matching Realm.BlizzardConnectedRealmId/Item.BlizzardItemId/EquippedItem.BlizzardItemId.
    public long BlizzardCharacterId { get; set; }
    public DateTimeOffset LastSyncedAt { get; set; }

    // One equipment snapshot per character (unique index on CharacterEquipment.CharacterId) —
    // the inverse side of the 1:1 relationship, so a character detail read can Include it.
    public CharacterEquipment? Equipment { get; set; }
}
