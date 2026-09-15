// Mirrors `AegisScribe.Domain.Managers.Models.Domain.ItemQuality`. Enums serialize as their name
// (Program.cs registers a JsonStringEnumConverter for both Mvc and minimal-API JSON options).
export type ItemQuality =
  | 'Poor'
  | 'Common'
  | 'Uncommon'
  | 'Rare'
  | 'Epic'
  | 'Legendary'
  | 'Artifact'
  | 'Heirloom';

// Mirrors `AegisScribe.Domain.Managers.Models.Domain.EquipmentSlot`.
export type EquipmentSlot =
  | 'Head'
  | 'Neck'
  | 'Shoulder'
  | 'Back'
  | 'Chest'
  | 'Wrist'
  | 'Hands'
  | 'Waist'
  | 'Legs'
  | 'Feet'
  | 'Finger1'
  | 'Finger2'
  | 'Trinket1'
  | 'Trinket2'
  | 'MainHand'
  | 'OffHand';

// Mirrors `EquippedItemServiceModel`.
export interface EquippedItemServiceModel {
  slot: EquipmentSlot;
  blizzardItemId: number;
  itemName: string;
  quality: ItemQuality;
  itemLevel: number;
  iconUrl: string | null;
}
