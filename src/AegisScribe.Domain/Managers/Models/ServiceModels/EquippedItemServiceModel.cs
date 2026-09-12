using AegisScribe.Domain.Managers.Models.Domain;

namespace AegisScribe.Domain.Managers.Models.ServiceModels;

public class EquippedItemServiceModel
{
    public EquipmentSlot Slot { get; set; }
    public long BlizzardItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public ItemQuality Quality { get; set; }
    public int ItemLevel { get; set; }

    // Absolute render.worldofwarcraft.com URL, composed server-side from the stored icon name so
    // the client references it directly rather than reconstructing Blizzard's CDN path itself.
    // See .claude/rules/frontend.md -> "Images come from Blizzard, referenced directly".
    public string? IconUrl { get; set; }
}
