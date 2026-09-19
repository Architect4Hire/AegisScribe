namespace AegisScribe.Domain.Managers.Models.Domain;

public class EquippedItem
{
    public Guid Id { get; set; }
    public Guid CharacterEquipmentId { get; set; }
    public CharacterEquipment CharacterEquipment { get; set; } = null!;
    public EquipmentSlot Slot { get; set; }

    // Not yet an FK to Item — the Item catalog arrives in 3.2.
    public long BlizzardItemId { get; set; }
    public string ItemName { get; set; } = null!;
    public ItemQuality Quality { get; set; }
    public int ItemLevel { get; set; }

    // The icon's file name on render.worldofwarcraft.com (today a numeric file id, e.g. "135349").
    // Resolved by the sync worker from /data/wow/media/item/{id}, not by the equipment fetch, which
    // carries no icon at all.
    public string? IconName { get; set; }

    // When Blizzard was last asked for this item's icon. Null with a null IconName means "not asked yet";
    // set with a null IconName means "asked, and there is none" (some items 404), so the worker does not
    // re-ask every pass. Refreshed inside the staleness window like every other Blizzard-derived value.
    public DateTimeOffset? IconSyncedAt { get; set; }
}
