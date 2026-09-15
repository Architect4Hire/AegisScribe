import { Component, computed, input } from '@angular/core';

const REFRESH_INTERVAL_DAYS = 30;
const MS_PER_DAY = 24 * 60 * 60 * 1000;

// Staleness is user-visible information, not an implementation detail (frontend.md). Every value here
// is real or directly derivable: lastSyncedAt is a ServiceModel field, refreshDue is that plus the
// 30-day Blizzard obligation, and source is the exact namespace shape for this endpoint, built from
// the region the request itself used. Nothing is invented.
@Component({
  imports: [],
  selector: 'scribe-provenance-panel',
  styleUrl: './provenance-panel.scss',
  templateUrl: './provenance-panel.html',
})
export class ProvenancePanel {
  readonly lastSyncedAt = input.required<string>();
  readonly region = input.required<string>();
  readonly characterId = input.required<string>();

  readonly refreshDue = computed(() => {
    const due = new Date(this.lastSyncedAt()).getTime() + REFRESH_INTERVAL_DAYS * MS_PER_DAY;
    return new Date(due).toISOString().slice(0, 10);
  });

  readonly source = computed(() => `profile-${this.region()}`);
}
