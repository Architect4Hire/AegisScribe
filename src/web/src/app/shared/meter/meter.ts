import { Component, computed, input } from '@angular/core';

export type MeterVariant = 'default' | 'partial';

@Component({
  imports: [],
  selector: 'scribe-meter',
  styleUrl: './meter.css',
  templateUrl: './meter.html',
})
export class Meter {
  readonly value = input.required<number>();
  readonly max = input.required<number>();
  readonly variant = input<MeterVariant>('default');

  // Optional because a meter sitting beside its own visible label (e.g. "Mythic" in a progression
  // row) would otherwise be double-announced; set it when the meter has no adjacent text label.
  readonly label = input<string>();

  readonly percent = computed(() => {
    const max = this.max();
    if (max <= 0) {
      return 0;
    }
    return Math.min(100, Math.max(0, (this.value() / max) * 100));
  });
}
