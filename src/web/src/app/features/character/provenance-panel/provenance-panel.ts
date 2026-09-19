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

  // In the viewer's own zone and labelled with it — this page has no community and so no community
  // zone to prefer, and an unlabelled time is the ambiguity frontend.md warns about. The raw ISO
  // instant stays one hover away on the <time> element, for anyone correlating with a log.
  //
  // Explicit fields rather than dateStyle/timeStyle: those two refuse to combine with timeZoneName and
  // throw, and the zone label is the one part this must not lose.
  readonly lastSyncedLabel = computed(() =>
    new Intl.DateTimeFormat(undefined, {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
      timeZoneName: 'short',
    }).format(new Date(this.lastSyncedAt())),
  );

  // A date, not an instant: the obligation is "within thirty days", not "by 11:21".
  readonly refreshDueDate = computed(
    () => new Date(new Date(this.lastSyncedAt()).getTime() + REFRESH_INTERVAL_DAYS * MS_PER_DAY),
  );

  readonly refreshDue = computed(() =>
    new Intl.DateTimeFormat(undefined, { dateStyle: 'medium' }).format(this.refreshDueDate()),
  );

  readonly refreshDueIso = computed(() => this.refreshDueDate().toISOString().slice(0, 10));

  readonly source = computed(() => `profile-${this.region()}`);
}
