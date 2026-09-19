import { Component, computed, input, signal } from '@angular/core';
import { EquipmentSlot, EquippedItemServiceModel } from '../../models/item.models';
import { SlotGlyph } from '../slot-glyph/slot-glyph';

// Sentence-case display labels for the meta line ("Main hand · Legendary"). Finger/trinket slots
// collapse to their game-facing name — nobody calls it "Finger1" in the UI.
const SLOT_LABELS: Record<EquipmentSlot, string> = {
  Head: 'Head',
  Neck: 'Neck',
  Shoulder: 'Shoulder',
  Back: 'Back',
  Chest: 'Chest',
  Wrist: 'Wrist',
  Hands: 'Hands',
  Waist: 'Waist',
  Legs: 'Legs',
  Feet: 'Feet',
  Finger1: 'Ring',
  Finger2: 'Ring',
  Trinket1: 'Trinket',
  Trinket2: 'Trinket',
  MainHand: 'Main hand',
  OffHand: 'Off hand',
};

@Component({
  imports: [SlotGlyph],
  selector: 'scribe-item-cell',
  styleUrl: './item-cell.css',
  templateUrl: './item-cell.html',
})
export class ItemCell {
  readonly slot = input.required<EquipmentSlot>();
  readonly item = input<EquippedItemServiceModel | null>(null);
  readonly showSlot = input(true);
  readonly flagged = input(false);

  // The URL that failed, not a flag. A cell is reused when the page moves to another character — the
  // route stays the same and only the inputs change — so a bare "failed" boolean from the previous
  // character's item would hide every later icon in this slot until a full reload.
  private readonly failedIconUrl = signal<string | null>(null);

  readonly slotLabel = computed(() => SLOT_LABELS[this.slot()]);

  // References the URL the API already resolved -- never reconstructs Blizzard's CDN path
  // client-side. See .claude/rules/frontend.md -> "Images come from Blizzard, referenced directly".
  readonly iconUrl = computed(() => {
    const iconUrl = this.item()?.iconUrl;
    return iconUrl && iconUrl !== this.failedIconUrl() ? iconUrl : null;
  });

  readonly itemClasses = computed(() => {
    const item = this.item();
    if (!item) {
      return 'is-empty';
    }
    const quality = `q-${item.quality.toLowerCase()}`;
    return this.flagged() ? `${quality} is-flagged` : quality;
  });

  readonly wowheadUrl = computed(() => {
    const item = this.item();
    return item ? `https://www.wowhead.com/item=${item.blizzardItemId}` : null;
  });

  onIconError(): void {
    this.failedIconUrl.set(this.item()?.iconUrl ?? null);
  }
}
