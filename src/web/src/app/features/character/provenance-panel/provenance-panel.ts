import { Component, computed, input } from '@angular/core';

const REFRESH_INTERVAL_DAYS = 30;
const MS_PER_DAY = 24 * 60 * 60 * 1000;

// Staleness is user-visible information, not an implementation detail (frontend.md -> "Every
// data-bound component ships four states" / "Degraded is... the one that matters most"). Every
// value here is real or directly derivable from real data:
// - lastSyncedAt: the ServiceModel field.
// - refreshDue: lastSyncedAt + 30 days, the Blizzard refresh obligation CLAUDE.md documents as a
//   hard ceiling on cache TTL -- not invented.
// - source: "profile-{region}", the exact Blizzard namespace shape for this endpoint (see
//   .claude/skills/add-external-sync/references/blizzard-endpoints.md), built from the region the
//   request itself used.
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
