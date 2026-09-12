import { Component, input } from '@angular/core';
import { Meter } from '../../../shared/meter/meter';
import { EmptyState } from '../../../shared/empty-state/empty-state';

// Shape anticipates the eventual ServiceModel -- no backend model for raid progression exists yet
// (nothing before Phase 6+ syncs it), so this mirrors nothing today. See the 5.7 plan.
export interface ProgressionRow {
  difficulty: string;
  killed: number;
  total: number;
}

@Component({
  imports: [EmptyState, Meter],
  selector: 'scribe-progression-panel',
  styleUrl: './progression-panel.scss',
  templateUrl: './progression-panel.html',
})
export class ProgressionPanel {
  readonly rows = input<ProgressionRow[]>([]);
}
