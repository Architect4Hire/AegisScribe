import { Component, DestroyRef, computed, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RosterService } from '../../../core/roster.service';
import {
  GuildRankNameServiceModel,
  RankViewModel,
  TenantRankServiceModel,
} from '../../../models/roster.models';
import { EmptyState } from '../../../shared/empty-state/empty-state';
import { RankPill } from '../../../shared/rank-pill/rank-pill';
import { Skeleton } from '../../../shared/skeleton/skeleton';

type LoadState = 'loading' | 'error' | 'loaded';

interface GuildRankGroup {
  guildId: string;
  guildName: string;
  ranks: GuildRankNameServiceModel[];
}

// The colour a new rank starts from, and the one hex literal in this feature.
//
// It is DATA rather than styling: the value is sent to the server as tenant config, stored on the
// rank, and surfaced later as --rank-color. The design system's rule governs stylesheets and forbids
// a class→hex map in TypeScript; a seed value for a colour input the officer immediately overwrites
// is neither, and it cannot be a var(--brass) reference because <input type="color"> needs a literal.
//
// It is NOT a claim to track --brass. SCSS and TypeScript cannot share a constant without a build
// step, so a comment promising to keep the two in sync by hand would be a promise nobody can keep.
// The consequence of drift here is cosmetic and self-correcting: if --brass moves, new ranks start at
// a slightly stale gold until somebody picks a different colour, which is the first thing most
// officers do anyway.
const DEFAULT_RANK_COLOUR = '#CBA76A';

// Two ladders on one screen (design/aegisscribe-armory.html §08), and keeping them together is the
// point: they are the two things called "rank" in this app, and seeing them side by side is what
// stops anyone believing one derives from the other.
//
//   The community's ladder — ours entirely. Create, reorder, recolour, delete.
//   The in-game rank names — labels for the 0-9 the GAME reports. Blizzard does not expose them, so
//   somebody has to type them, and this is where.
@Component({
  imports: [EmptyState, RankPill, Skeleton],
  selector: 'scribe-rank-manager',
  styleUrl: './rank-manager.scss',
  templateUrl: './rank-manager.html',
})
export class RankManager {
  private readonly rosterService = inject(RosterService);
  // Every subscription below is piped through takeUntilDestroyed(this.destroyRef). These are one-shot
  // HttpClient observables, so this is not about a classic leak — it is about a late callback setting
  // signals on a component the user has already navigated away from (frontend.md).
  private readonly destroyRef = inject(DestroyRef);

  readonly tenantSlug = input.required<string>();

  readonly state = signal<LoadState>('loading');
  readonly ranks = signal<TenantRankServiceModel[]>([]);
  readonly guildRankNames = signal<GuildRankNameServiceModel[]>([]);

  readonly newRankName = signal('');
  readonly newRankColour = signal(DEFAULT_RANK_COLOUR);

  // A failed WRITE, reported inline and separately from `state`. Deliberately not the same signal:
  // `state` gates whether the editor renders at all, so setting it here would wipe the ladder an
  // officer is halfway through editing because one save failed. Cleared by the next success.
  readonly writeError = signal<string | null>(null);

  // The server refuses a duplicate name with a 409; this is the same answer offered before the
  // request, so the common mistake never costs a round trip.
  readonly duplicateName = computed(() => {
    const candidate = this.newRankName().trim().toLowerCase();

    return candidate !== '' && this.ranks().some((rank) => rank.name.toLowerCase() === candidate);
  });

  readonly canCreate = computed(() => this.newRankName().trim() !== '' && !this.duplicateName());

  // Grouped for display, because a community can follow several guilds and each has its own ten.
  readonly guildGroups = computed<GuildRankGroup[]>(() => {
    const groups = new Map<string, GuildRankGroup>();

    for (const row of this.guildRankNames()) {
      const group = groups.get(row.guildId) ?? {
        guildId: row.guildId,
        guildName: row.guildName,
        ranks: [],
      };
      group.ranks.push(row);
      groups.set(row.guildId, group);
    }

    return [...groups.values()].map((group) => ({
      ...group,
      ranks: [...group.ranks].sort((left, right) => left.rank - right.rank),
    }));
  });

  constructor() {
    effect(() => {
      this.load(this.tenantSlug());
    });
  }

  retry(): void {
    this.load(this.tenantSlug());
  }

  createRank(): void {
    if (!this.canCreate()) {
      return;
    }

    // Appended at the end of the ladder. Ties are broken by name server-side, so a shared position is
    // a display tie rather than a data error — but starting past the current last avoids one anyway.
    const viewModel: RankViewModel = {
      name: this.newRankName().trim(),
      sortOrder: Math.min(this.nextSortOrder(), 999),
      colour: this.newRankColour(),
    };

    this.rosterService.createRank(this.tenantSlug(), viewModel)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
      next: (rank) => {
        this.writeError.set(null);
        this.ranks.update((ranks) => [...ranks, rank]);
        this.newRankName.set('');
        this.newRankColour.set(DEFAULT_RANK_COLOUR);
      },
      // The typed name stays in the box, so a failed save does not also lose the officer's input.
      error: () => this.writeError.set("Couldn't create that rank. Try again."),
    });
  }

  recolour(rank: TenantRankServiceModel, colour: string): void {
    this.update(rank, { ...this.toViewModel(rank), colour });
  }

  rename(rank: TenantRankServiceModel, name: string): void {
    const trimmed = name.trim();

    if (trimmed === '' || trimmed === rank.name) {
      return;
    }

    this.update(rank, { ...this.toViewModel(rank), name: trimmed });
  }

  // Reordering is a swap of sort orders with the neighbour, which keeps every other rank's position
  // untouched — a renumber-everything approach would rewrite rows nobody asked to change.
  move(rank: TenantRankServiceModel, direction: -1 | 1): void {
    const ordered = this.orderedRanks();
    const index = ordered.findIndex((candidate) => candidate.id === rank.id);
    const neighbour = ordered[index + direction];

    if (!neighbour) {
      return;
    }

    this.update(rank, { ...this.toViewModel(rank), sortOrder: neighbour.sortOrder });
    this.update(neighbour, { ...this.toViewModel(neighbour), sortOrder: rank.sortOrder });
  }

  deleteRank(rank: TenantRankServiceModel): void {
    this.rosterService.deleteRank(this.tenantSlug(), rank.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
      next: () => {
        this.writeError.set(null);
        this.ranks.update((ranks) => ranks.filter((candidate) => candidate.id !== rank.id));
      },
      // A 409 here means roster entries still hold this rank — the server refuses rather than
      // un-ranking everyone who held it, and that refusal is worth reading rather than swallowing.
      // It is by far the likeliest failure on this button, so the message names it.
      error: () =>
        this.writeError.set(
          `Couldn't delete ${rank.name}. Characters on the roster may still hold it — move them to another rank first.`,
        ),
    });
  }

  setGuildRankName(row: GuildRankNameServiceModel, name: string): void {
    const trimmed = name.trim();
    const value = trimmed === '' ? null : trimmed;

    if (value === row.name) {
      return;
    }

    this.rosterService.setGuildRankName(this.tenantSlug(), row.guildId, row.rank, value)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
      next: () =>
        this.guildRankNames.update((rows) =>
          rows.map((candidate) =>
            candidate.guildId === row.guildId && candidate.rank === row.rank
              ? { ...candidate, name: value }
              : candidate,
          ),
        ),
      error: () =>
        this.writeError.set(`Couldn't save the name for ${row.guildName} rank ${row.rank}.`),
    });
  }

  readonly orderedRanks = computed(() =>
    [...this.ranks()].sort(
      (left, right) => left.sortOrder - right.sortOrder || left.name.localeCompare(right.name),
    ),
  );

  private nextSortOrder(): number {
    const ordered = this.orderedRanks();

    return ordered.length === 0 ? 0 : ordered[ordered.length - 1].sortOrder + 10;
  }

  private toViewModel(rank: TenantRankServiceModel): RankViewModel {
    return { name: rank.name, sortOrder: rank.sortOrder, colour: rank.colour };
  }

  private update(rank: TenantRankServiceModel, viewModel: RankViewModel): void {
    this.rosterService.updateRank(this.tenantSlug(), rank.id, viewModel)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
      next: (updated) => {
        this.writeError.set(null);
        this.ranks.update((ranks) =>
          ranks.map((candidate) => (candidate.id === updated.id ? updated : candidate)),
        );
      },
      // Covers rename, recolour and reorder. The ladder stays on screen; only the one change failed.
      error: () => this.writeError.set(`Couldn't save the change to ${rank.name}. Try again.`),
    });
  }

  private load(tenantSlug: string): void {
    this.state.set('loading');

    this.rosterService.listRanks(tenantSlug)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
      next: (ranks) => {
        this.ranks.set(ranks);
        this.state.set('loaded');
      },
      error: () => this.state.set('error'),
    });

    // A community that follows no guilds simply has no in-game ranks to name, which is an empty
    // section rather than a failure — so this never touches the screen's state.
    this.rosterService.listGuildRankNames(tenantSlug)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
      next: (rows) => this.guildRankNames.set(rows),
      error: () => this.guildRankNames.set([]),
    });
  }
}
