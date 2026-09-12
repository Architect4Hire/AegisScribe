import { Component, input } from '@angular/core';
import { Meter } from '../../../shared/meter/meter';
import { EmptyState } from '../../../shared/empty-state/empty-state';

// Shape anticipates the eventual ServiceModel -- no per-character profession/skill-level data
// model exists yet (the Profession/Recipe catalog is global, with no character join). See the 5.7
// plan. The reference's "N craftable upgrades" line needs the crafting advisor (Phase 13) and is
// deliberately not built here -- there's nothing real to compute it from yet.
export interface ProfessionRow {
  name: string;
  skillLevel: number;
  maxSkillLevel: number;
}

@Component({
  imports: [EmptyState, Meter],
  selector: 'scribe-profession-panel',
  styleUrl: './profession-panel.scss',
  templateUrl: './profession-panel.html',
})
export class ProfessionPanel {
  readonly rows = input<ProfessionRow[]>([]);
}
