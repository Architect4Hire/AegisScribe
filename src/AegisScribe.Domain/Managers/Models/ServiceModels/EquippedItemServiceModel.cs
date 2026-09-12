using AegisScribe.Domain.Managers.Models.Domain;

namespace AegisScribe.Domain.Managers.Models.ServiceModels;

public class EquippedItemServiceModel
{
    public EquipmentSlot Slot { get; set; }
    public long BlizzardItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public ItemQuality Quality { get; set; }
    public int ItemLevel { get; set; }
    public string? IconName { get; set; }
}
