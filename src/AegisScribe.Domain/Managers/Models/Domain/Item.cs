namespace AegisScribe.Domain.Managers.Models.Domain;

public class Item
{
    public Guid Id { get; set; }
    public long BlizzardItemId { get; set; }
    public string Name { get; set; } = null!;
    public ItemQuality Quality { get; set; }
    public EquipmentSlot? Slot { get; set; }
    public int ItemLevel { get; set; }
    public string SearchText { get; set; } = null!;
    public DateTimeOffset LastSyncedAt { get; set; }

    public string ComposeSearchText() => Slot is { } slot
        ? $"{Name} — {Quality} {SlotLabel(slot)}, item level {ItemLevel}"
        : $"{Name} — {Quality} item, item level {ItemLevel}";

    private static string SlotLabel(EquipmentSlot slot) => slot switch
    {
        EquipmentSlot.Head => "helm",
        EquipmentSlot.Neck => "necklace",
        EquipmentSlot.Shoulder => "shoulder armor",
        EquipmentSlot.Back => "cloak",
        EquipmentSlot.Chest => "chest armor",
        EquipmentSlot.Wrist => "bracers",
        EquipmentSlot.Hands => "gloves",
        EquipmentSlot.Waist => "belt",
        EquipmentSlot.Legs => "leg armor",
        EquipmentSlot.Feet => "boots",
        EquipmentSlot.Finger1 or EquipmentSlot.Finger2 => "ring",
        EquipmentSlot.Trinket1 or EquipmentSlot.Trinket2 => "trinket",
        EquipmentSlot.MainHand => "main-hand weapon",
        EquipmentSlot.OffHand => "off-hand item",
        _ => "item",
    };
}
