import { Component, computed, input } from '@angular/core';
import { CharacterDetailServiceModel } from '../../../models/character.models';
import { StatTile } from '../../../shared/stat-tile/stat-tile';
import { StatusPill } from '../../../shared/status-pill/status-pill';

const MS_PER_MINUTE = 60 * 1000;
const MS_PER_HOUR = 60 * MS_PER_MINUTE;
const MS_PER_DAY = 24 * MS_PER_HOUR;

@Component({
  imports: [StatTile, StatusPill],
  selector: 'scribe-character-banner',
  styleUrl: './character-banner.scss',
  templateUrl: './character-banner.html',
})
export class CharacterBanner {
  readonly character = input.required<CharacterDetailServiceModel>();
  // Known from the route that fetched this character, not the response body -- see the 5.7 plan.
  readonly region = input.required<string>();

  readonly portraitInitial = computed(() => this.character().name.charAt(0).toUpperCase());

  // "argent-dawn" -> "Argent Dawn". The API only carries the slug; this is a display transform of
  // real data, not an invented name.
  readonly realmLabel = computed(() =>
    this.character()
      .realmSlug.split('-')
      .map((word) => word.charAt(0).toUpperCase() + word.slice(1))
      .join(' '),
  );

  // "Stale Nd" once degraded -- never sets a fresh-looking pill over data that's actually behind.
  // Nothing can set isDegraded true before Phase 6, but the branch is real and tested now.
  readonly syncPillState = computed(() => (this.character().isDegraded ? 'attention' : 'ok'));

  readonly syncLabel = computed(() => {
    const character = this.character();
    const ageMs = Date.now() - new Date(character.lastSyncedAt).getTime();
    return character.isDegraded ? `Stale ${formatAge(ageMs)}` : `Synced ${formatAge(ageMs)} ago`;
  });
}

function formatAge(ageMs: number): string {
  if (ageMs < MS_PER_HOUR) {
    return `${Math.max(1, Math.round(ageMs / MS_PER_MINUTE))}m`;
  }
  if (ageMs < MS_PER_DAY) {
    return `${Math.round(ageMs / MS_PER_HOUR)}h`;
  }
  return `${Math.round(ageMs / MS_PER_DAY)}d`;
}
