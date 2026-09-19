import { Component, computed, input } from '@angular/core';
import { EquipmentSlot } from '../../models/item.models';

type Glyph = 'head' | 'neck' | 'shoulder' | 'chest' | 'hand' | 'leg' | 'ring' | 'trinket' | 'weapon';

// Which outline stands for which slot, as design/aegisscribe-armory.html's S1 screen draws them —
// including its choices of the chest outline for Back and the leg outline for Waist and Feet. Nine
// shapes for sixteen slots is the reference's own economy, not a gap.
const GLYPH_FOR_SLOT: Record<EquipmentSlot, Glyph> = {
  Head: 'head',
  Neck: 'neck',
  Shoulder: 'shoulder',
  Back: 'chest',
  Chest: 'chest',
  Wrist: 'hand',
  Hands: 'hand',
  Waist: 'leg',
  Legs: 'leg',
  Feet: 'leg',
  Finger1: 'ring',
  Finger2: 'ring',
  Trinket1: 'trinket',
  Trinket2: 'trinket',
  MainHand: 'weapon',
  OffHand: 'weapon',
};

// The slot's outline, drawn where an item icon would be when there isn't one: an empty slot, an item
// whose icon Blizzard has not been asked for yet, or one of the icons that genuinely 404s. Transcribed
// from the reference's <symbol id="g-*"> set. Stroked in currentColor, so the tile's ink decides the
// colour and nothing here invents one.
@Component({
  imports: [],
  selector: 'scribe-slot-glyph',
  templateUrl: './slot-glyph.html',
  host: { 'aria-hidden': 'true' },
})
export class SlotGlyph {
  readonly slot = input.required<EquipmentSlot>();
  readonly size = input(18);

  readonly glyph = computed(() => GLYPH_FOR_SLOT[this.slot()]);
}
