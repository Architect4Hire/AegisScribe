import { Component, computed, input } from '@angular/core';
import { EquipmentSlot, EquippedItemServiceModel } from '../../models/item.models';
import { ItemCell } from '../item-cell/item-cell';

// A vertical column of equipment slots. A slot with no matching entry in `equipment` renders as
// item-cell's empty state automatically -- the rail never has to know which slots are filled.
@Component({
  imports: [ItemCell],
  selector: 'scribe-equipment-rail',
  styleUrl: './equipment-rail.css',
  templateUrl: './equipment-rail.html',
})
export class EquipmentRail {
  readonly slots = input.required<EquipmentSlot[]>();
  readonly equipment = input.required<EquippedItemServiceModel[]>();
  readonly side = input.required<'left' | 'right'>();

  readonly ariaLabel = computed(() => (this.side() === 'left' ? 'Left equipment' : 'Right equipment'));

  itemFor(slot: EquipmentSlot): EquippedItemServiceModel | null {
    return this.equipment().find((item) => item.slot === slot) ?? null;
  }
}
