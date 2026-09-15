import { Component, computed, input, output } from '@angular/core';
import { CharacterDetailServiceModel } from '../../../models/character.models';
import { CharacterClaimServiceModel } from '../../../models/roster.models';
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

  // Claim state, and everything below it, is OPTIONAL. Null means there is no community in
  // context — the tenant-less front door — and the banner then shows no pill and no button at all.
  // Absent inputs leave this component rendering exactly as it did before claiming existed.
  readonly claim = input<CharacterClaimServiceModel | null>(null);

  // The signed-in user, for the one comparison that decides "mine". The API deliberately returns the
  // holder's id rather than an isMine flag, because that response is cached per tenant.
  readonly currentUserId = input<string | null>(null);

  // Officers may free somebody else's claim. Cosmetics — the endpoint carries TenantOfficer itself.
  readonly canClear = input(false);

  readonly claimed = output<void>();
  readonly released = output<void>();
  readonly cleared = output<void>();

  readonly portraitInitial = computed(() => this.character().name.charAt(0).toUpperCase());

  // "argent-dawn" -> "Argent Dawn". The API only carries the slug; this is a display transform of
  // real data, not an invented name.
  readonly realmLabel = computed(() =>
    this.character()
      .realmSlug.split('-')
      .map((word) => word.charAt(0).toUpperCase() + word.slice(1))
      .join(' '),
  );

  // Null on the tenant-less route: with no community in context there is nothing to claim a character
  // FOR, so the whole control set is absent rather than disabled.
  readonly showsClaimState = computed(() => this.claim() !== null);

  readonly claimedByMe = computed(() => {
    const holder = this.claim()?.claimedByUserId;

    return holder !== null && holder !== undefined && holder === this.currentUserId();
  });

  readonly claimedBySomebodyElse = computed(
    () => this.claim()?.claimedByUserId != null && !this.claimedByMe(),
  );

  readonly isUnclaimed = computed(
    () => this.showsClaimState() && this.claim()?.claimedByUserId == null,
  );

  // Falls back to a bare "Claimed" when the holder has set no display name — better than rendering
  // an empty label, and their email is not a fellow member's to see.
  readonly claimedByLabel = computed(() =>
    this.claimedByMe()
      ? 'Claimed by you'
      : `Claimed by ${this.claim()?.claimedByDisplayName ?? 'another member'}`,
  );

  // "Stale Nd" once degraded -- never sets a fresh-looking pill over data that's actually behind.
  readonly syncPillState = computed(() => (this.character().isDegraded ? 'attention' : 'ok'));

  readonly syncLabel = computed(() => {
    const character = this.character();
    const ageMs = Date.now() - new Date(character.lastSyncedAt).getTime();
    return character.isDegraded ? `Stale ${formatAge(ageMs)}` : `Synced ${formatAge(ageMs)} ago`;
  });
}

function formatAge(ageMs: number): string {
  if (ageMs >= MS_PER_DAY) {
    return `${Math.floor(ageMs / MS_PER_DAY)}d`;
  }

  if (ageMs >= MS_PER_HOUR) {
    return `${Math.floor(ageMs / MS_PER_HOUR)}h`;
  }

  return `${Math.max(Math.floor(ageMs / MS_PER_MINUTE), 1)}m`;
}
