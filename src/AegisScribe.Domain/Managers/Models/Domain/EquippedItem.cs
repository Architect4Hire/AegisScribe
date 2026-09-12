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
    public string? IconName { get; set; }
}
