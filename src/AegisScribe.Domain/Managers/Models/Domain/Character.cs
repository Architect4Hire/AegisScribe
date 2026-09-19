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

    // From the character-media endpoint, refreshed with the character rather than on a schedule of its
    // own (blizzard-endpoints.md: a transmog change alters the render). Null URLs with a MediaSyncedAt
    // mean Blizzard was asked and has no render; a null MediaSyncedAt means nobody has asked yet, which
    // is what the sync worker's media backfill selects on.
    public string? AvatarUrl { get; set; }
    public string? RenderUrl { get; set; }
    public DateTimeOffset? MediaSyncedAt { get; set; }

    public void ApplyMedia(CharacterMedia media, DateTimeOffset syncedAt)
    {
        AvatarUrl = media.AvatarUrl;
        RenderUrl = media.RenderUrl;
        MediaSyncedAt = syncedAt;
    }

    // One equipment snapshot per character (unique index on CharacterEquipment.CharacterId) —
    // the inverse side of the 1:1 relationship, so a character detail read can Include it.
    public CharacterEquipment? Equipment { get; set; }
}
