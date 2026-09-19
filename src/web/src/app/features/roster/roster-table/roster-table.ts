import { Component, DestroyRef, computed, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { ClaimService } from '../../../core/claim.service';
import { CurrentUserService } from '../../../core/current-user.service';
import { RosterService } from '../../../core/roster.service';
import {
  RosterEntryServiceModel,
  RosterSort,
  TenantRankServiceModel,
} from '../../../models/roster.models';
import { FirstRunChecklist } from '../../tenancy/first-run-checklist/first-run-checklist';
import { AddCharacter } from '../add-character/add-character';
import { EmptyState } from '../../../shared/empty-state/empty-state';
import { RankPill } from '../../../shared/rank-pill/rank-pill';
import { Skeleton } from '../../../shared/skeleton/skeleton';
import { StatusPill, StatusPillState } from '../../../shared/status-pill/status-pill';

type LoadState = 'loading' | 'empty' | 'error' | 'loaded';

interface SortOption {
  id: RosterSort;
  label: string;
}

// Each has a FIXED direction server-side — item level descends because nobody reads a roster
// worst-geared-first — so there is no direction toggle to render.
const SORTS: SortOption[] = [
  { id: 'Rank', label: 'Rank' },
  { id: 'Name', label: 'Character' },
  { id: 'ItemLevel', label: 'Item level' },
];

// Past this, a character's data is old enough to say so rather than present it as current. Well inside
// the thirty-day refresh obligation, so the badge appears long before compliance is at stake.
const STALE_AFTER_DAYS = 7;

const MS_PER_DAY = 24 * 60 * 60 * 1000;

// The roster (design reference screen S3).
//
// Two rank columns side by side, which is the point of the screen: the community's own ladder and the
// rank the game reports are different facts, and neither derives from the other (tenancy.md). They are
// rendered differently on purpose — a pill for the community's, plain mono text for the game's — so
// nothing implies one is computed from the other.
//
// It also hosts the first-run checklist (8.5), which is why this screen is where /t/{slug} redirects:
// the roster is a community's landing page, and the checklist is an aside above it that hides itself
// for a plain member, for a set-up community, and for anyone who has dismissed it.
@Component({
  imports: [AddCharacter, EmptyState, FirstRunChecklist, RankPill, RouterLink, Skeleton, StatusPill],
  selector: 'scribe-roster-table',
  styleUrl: './roster-table.scss',
  templateUrl: './roster-table.html',
})
export class RosterTable {
  private readonly rosterService = inject(RosterService);
  private readonly claimService = inject(ClaimService);
  private readonly currentUser = inject(CurrentUserService);
  // Every subscription below is piped through takeUntilDestroyed(this.destroyRef). These are one-shot
  // HttpClient observables, so this is not a classic leak — it is about a late callback setting signals
  // on a component the user has already navigated away from.
  private readonly destroyRef = inject(DestroyRef);

  // From the router, never a stored variable — the URL is the source of truth for which community this
  // is.
  readonly tenantSlug = input.required<string>();

  readonly sorts = SORTS;

  readonly state = signal<LoadState>('loading');
  readonly rows = signal<RosterEntryServiceModel[]>([]);
  readonly ranks = signal<TenantRankServiceModel[]>([]);
  readonly sort = signal<RosterSort>('Rank');
  readonly nextCursor = signal<string | null>(null);
  readonly loadingMore = signal(false);

  // Which row's note is open for editing. Null when none is.
  readonly editingNoteFor = signal<string | null>(null);

  // A failed WRITE, reported inline and deliberately not the same signal as `state`: `state` gates
  // whether the table renders at all, so setting it here would replace a roster we loaded successfully
  // with a full-page error because one rank assignment failed.
  readonly writeError = signal<string | null>(null);

  // The one comparison that decides "mine". The API returns the holder's id rather than an isMine flag
  // precisely so this is the client's job — a per-caller field could not be cached.
  readonly currentUserId = computed(() => this.currentUser.user()?.id ?? null);

  claimedByMe(entry: RosterEntryServiceModel): boolean {
    return entry.claimedByUserId !== null && entry.claimedByUserId === this.currentUserId();
  }

  claimedBySomebodyElse(entry: RosterEntryServiceModel): boolean {
    return entry.claimedByUserId !== null && !this.claimedByMe(entry);
  }

  // Falls back to a bare label when the holder has set no display name — their email is not a fellow
  // member's to see.
  claimedByLabel(entry: RosterEntryServiceModel): string {
    return this.claimedByMe(entry) ? 'You' : (entry.claimedByDisplayName ?? 'Another member');
  }

  claimCharacter(entry: RosterEntryServiceModel): void {
    this.claimService
      .claim(this.tenantSlug(), entry.characterId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (claim) => {
          this.writeError.set(null);
          this.applyClaim(entry.id, claim.claimedByUserId, claim.claimedByDisplayName);
        },
        // A 409 means somebody claimed it first — the likeliest failure here, so the message says so.
        error: () =>
          this.writeError.set(
            `Couldn't claim ${entry.characterName}. Somebody else may have claimed it first.`,
          ),
      });
  }

  releaseClaim(entry: RosterEntryServiceModel): void {
    this.claimService
      .release(this.tenantSlug(), entry.characterId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.writeError.set(null);
          this.applyClaim(entry.id, null, null);
        },
        error: () => this.writeError.set(`Couldn't release the claim on ${entry.characterName}.`),
      });
  }

  // Officers only, and it FREES the claim rather than taking it — the endpoint refuses to reassign.
  clearClaim(entry: RosterEntryServiceModel): void {
    this.claimService
      .clearHolder(this.tenantSlug(), entry.characterId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.writeError.set(null);
          this.applyClaim(entry.id, null, null);
        },
        error: () => this.writeError.set(`Couldn't clear the claim on ${entry.characterName}.`),
      });
  }

  private applyClaim(
    rosterEntryId: string,
    userId: string | null,
    displayName: string | null,
  ): void {
    this.rows.update((rows) =>
      rows.map((row) =>
        row.id === rosterEntryId
          ? { ...row, claimedByUserId: userId, claimedByDisplayName: displayName }
          : row,
      ),
    );
  }

  // Role-conditional UI is cosmetics — every write below has a TenantOfficer policy behind it
  // (auth.md). Hiding the controls is about not offering what will be refused.
  readonly isOfficer = computed(() => {
    const membership = this.currentUser
      .user()
      ?.memberships.find((entry) => entry.tenantSlug === this.tenantSlug());

    return membership?.role === 'Officer' || membership?.role === 'Owner';
  });

  constructor() {
    effect(() => {
      this.load(this.tenantSlug(), this.sort());
    });
  }

  selectSort(sort: RosterSort): void {
    if (sort !== this.sort()) {
      // Clearing the cursor is load-bearing: a position from one ordering means nothing in another.
      this.nextCursor.set(null);
      this.sort.set(sort);
    }
  }

  retry(): void {
    this.load(this.tenantSlug(), this.sort());
  }

  loadMore(): void {
    const cursor = this.nextCursor();

    if (!cursor || this.loadingMore()) {
      return;
    }

    this.loadingMore.set(true);
    this.rosterService
      .listRoster(this.tenantSlug(), { sort: this.sort(), cursor })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (page) => {
          this.rows.update((existing) => [...existing, ...page.items]);
          this.nextCursor.set(page.hasMore ? page.nextCursor : null);
          this.loadingMore.set(false);
        },
        error: () => {
          // A failed page-two keeps page one on screen. Replacing a working roster with an error screen
          // because the NEXT page failed is the degraded-state mistake in miniature.
          this.loadingMore.set(false);
          this.nextCursor.set(null);
        },
      });
  }

  assignRank(entry: RosterEntryServiceModel, rankId: string): void {
    const tenantRankId = rankId === '' ? null : rankId;

    this.rosterService
      .setRank(this.tenantSlug(), entry.id, tenantRankId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.writeError.set(null);
          this.applyRank(entry.id, tenantRankId);
        },
        // Inline, with the roster left on screen. A 403 here is real rather than hypothetical: an officer
        // demoted while this page was open still has the controls rendered.
        error: () =>
          this.writeError.set(
            `Couldn't change ${entry.characterName}'s rank. Your changes to other rows were saved.`,
          ),
      });
  }

  startEditingNote(entry: RosterEntryServiceModel): void {
    this.editingNoteFor.set(entry.id);
  }

  cancelEditingNote(): void {
    this.editingNoteFor.set(null);
  }

  saveNote(entry: RosterEntryServiceModel, note: string): void {
    const trimmed = note.trim();
    const officerNote = trimmed === '' ? null : trimmed;

    this.rosterService
      .setOfficerNote(this.tenantSlug(), entry.id, officerNote)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.writeError.set(null);
          this.rows.update((rows) =>
            rows.map((row) => (row.id === entry.id ? { ...row, officerNote } : row)),
          );
          this.editingNoteFor.set(null);
        },
        // The editor stays open on failure, so the text the officer typed is not thrown away along with
        // the request that failed to save it.
        error: () =>
          this.writeError.set(`Couldn't save the note on ${entry.characterName}. Try again.`),
      });
  }

  // "Rank 3", or the community's own name for it once somebody has typed one. Blizzard does not expose
  // guild rank names, so an unnamed rank stays a number rather than getting a label we invented.
  inGameRankLabel(entry: RosterEntryServiceModel): string | null {
    if (entry.blizzardRank === null) {
      return null;
    }

    return entry.blizzardRankName ?? `Rank ${entry.blizzardRank}`;
  }

  // The guild the in-game rank belongs to, so a community following more than one is never left
  // guessing which this column means.
  inGameRankTitle(entry: RosterEntryServiceModel): string {
    return entry.guildName ? `In-game rank in ${entry.guildName}` : '';
  }

  isStale(entry: RosterEntryServiceModel): boolean {
    return this.ageInDays(entry) >= STALE_AFTER_DAYS;
  }

  // The DEGRADED state, per row rather than per screen: we hold this character's data and it is old.
  // Shown with its age rather than hidden — a blank error over data we still have is the
  // characteristic mistake in this app.
  syncState(entry: RosterEntryServiceModel): StatusPillState {
    return this.isStale(entry) ? 'attention' : 'ok';
  }

  syncLabel(entry: RosterEntryServiceModel): string {
    const days = this.ageInDays(entry);

    if (this.isStale(entry)) {
      return `Stale ${days}d`;
    }

    return days === 0 ? 'Synced today' : `Synced ${days}d ago`;
  }

  private ageInDays(entry: RosterEntryServiceModel): number {
    return Math.floor((Date.now() - new Date(entry.lastSyncedAt).getTime()) / MS_PER_DAY);
  }

  private applyRank(rosterEntryId: string, tenantRankId: string | null): void {
    const rank = this.ranks().find((candidate) => candidate.id === tenantRankId) ?? null;

    this.rows.update((rows) =>
      rows.map((row) =>
        row.id === rosterEntryId
          ? {
              ...row,
              rankId: rank?.id ?? null,
              rankName: rank?.name ?? null,
              rankColour: rank?.colour ?? null,
              rankSortOrder: rank?.sortOrder ?? null,
            }
          : row,
      ),
    );
  }

  private load(tenantSlug: string, sort: RosterSort): void {
    this.state.set('loading');

    this.rosterService
      .listRoster(tenantSlug, { sort })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (page) => {
          this.rows.set(page.items);
          this.nextCursor.set(page.hasMore ? page.nextCursor : null);
          this.state.set(page.items.length === 0 ? 'empty' : 'loaded');
        },
        error: () => {
          this.rows.set([]);
          // Every failure here is an error rather than an empty roster — including a 404, which means
          // this community is not one of ours and which the tenant guard normally catches first. An
          // empty roster is a 200 with no rows, handled above.
          this.state.set('error');
        },
      });

    // The ladder, for the officer rank picker. A failure here costs the picker its options and nothing
    // else, so it never touches the screen's own state.
    this.rosterService
      .listRanks(tenantSlug)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (ranks) => this.ranks.set(ranks),
        error: () => this.ranks.set([]),
      });
  }
}
