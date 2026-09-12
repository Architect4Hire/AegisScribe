import { Component, input, output } from '@angular/core';

@Component({
  imports: [],
  selector: 'scribe-filter-chip',
  styleUrl: './filter-chip.css',
  templateUrl: './filter-chip.html',
})
export class FilterChip {
  readonly key = input.required<string>();
  readonly value = input.required<string>();
  readonly removed = output<void>();
}
