import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { CurrentUserService } from '../../../core/current-user.service';
import { GuildService } from '../../../core/guild.service';
import { REGIONS, toRealmSlug } from '../../../core/realm';
import { RosterService } from '../../../core/roster.service';
import { TenantService } from '../../../core/tenant.service';
import { GuildServiceModel, RosterImportServiceModel } from '../../../models/guild.models';
import { EmptyState } from '../../../shared/empty-state/empty-state';
import { Skeleton } from '../../../shared/skeleton/skeleton';
import { StatusPill, StatusPillState } from '../../../shared/status-pill/status-pill';

type LoadState = 'loading' | 'error' | 'loaded';
type GuildAction = 'importing' | 'syncing' | 'unlinking';

// Past this, the guild's roster is old enough to say so. Matches the roster's own threshold, and sits
// well inside the thirty-day refresh obligation the sync worker keeps.
const STALE_AFTER_DAYS = 7;

const MS_PER_DAY = 24 * 60 * 60 * 1000;

// The problem type the API puts on a 429 that means "this community's budget is spent", as opposed to
// the API's own per-IP throttling, which is also a 429 and means something else.
const BUDGET_EXHAUSTED_TYPE = 'tenant-sync-budget-exhausted';

// The guilds a community follows — the screen the first-run checklist's "Link your guild" step leads
// to, and the only place in the browser that asks the API to fetch anything from Blizzard.
//
// Every member can see the list. Linking, importing, re-syncing and unlinking are officer acts: the
// controls are hidden for a plain member, and the TenantOfficer policies behind them are what actually
// refuse (auth.md). Nothing here calls Blizzard itself — the API makes the call, under the community's
// sync budget, and persists the answer.
@Component({
  imports: [EmptyState, RouterLink, Skeleton, StatusPill],
  selector: 'scribe-guild-overview',
  styleUrl: './guild-overview.scss',
  templateUrl: './guild-overview.html',
})
export class GuildOverview {
  private readonly guildService = inject(GuildService);
  private readonly rosterService = inject(RosterService);
  private readonly tenantService = inject(TenantService);
  private readonly currentUser = inject(CurrentUserService);
  // One-shot HttpClient observables throughout, so this is about a late callback setting signals on a
  // component the user has already left, not a classic leak.
  private readonly destroyRef = inject(DestroyRef);

  // From the router — the URL is the source of truth for which community this is.
  readonly tenantSlug = input.required<string>();

  readonly regions = REGIONS;

  readonly state = signal<LoadState>('loading');
  readonly guilds = signal<GuildServiceModel[]>([]);

  // True until we learn otherwise: the note about missing credentials is only shown on a positive
  // answer, so a failed overview read never tells an officer something that may not be true.
  readonly blizzardConfigured = signal(true);

  readonly region = signal<string>('us');
  readonly realm = signal('');
  readonly guildName = signal('');
  readonly linking = signal(false);

  // Per guild, so one row's import does not disable another's buttons.
  readonly busy = signal<Record<string, GuildAction>>({});
  readonly importResults = signal<Record<string, RosterImportServiceModel>>({});

  // A write's outcome, kept apart from `state` so a failed link never replaces a list we loaded.
  readonly notice = signal<string | null>(null);
  readonly writeError = signal<string | null>(null);

  readonly isOfficer = computed(() => {
    const role = this.currentUser
      .user()
      ?.memberships.find((membership) => membership.tenantSlug === this.tenantSlug())?.role;

    return role === 'Officer' || role === 'Owner';
  });

  readonly canLink = computed(
    () => !this.linking() && toRealmSlug(this.realm()) !== '' && this.guildName().trim() !== '',
  );

  readonly rosterLink = computed(() => ['/t', this.tenantSlug(), 'roster']);

  readonly emptyBody = computed(() =>
    this.isOfficer()
      ? 'Link your guild above and its members can be imported onto the roster.'
      : "An officer can link this community's guild in game. Until then the roster is built by hand.",
  );

  constructor() {
    effect(() => {
      this.load(this.tenantSlug(), this.isOfficer());
    });
  }

  retry(): void {
    this.load(this.tenantSlug(), this.isOfficer());
  }

  link(): void {
    if (!this.canLink()) {
      return;
    }

    const region = this.region();
    const realmSlug = toRealmSlug(this.realm());
    const guildName = this.guildName().trim();

    this.linking.set(true);
    this.clearMessages();

    this.guildService
      .link(this.tenantSlug(), { region, realmSlug, guildName })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (guild) => {
          this.linking.set(false);
          this.upsert(guild);
          this.guildName.set('');
          this.notice.set(
            `Linked ${guild.name} — ${memberCountLabel(guild.memberCount)} fetched from Blizzard. ` +
              'Import them to put them on the roster.',
          );
        },
        // The typed values stay in the form, so a typo is a quick fix rather than a retype.
        error: (error: unknown) => {
          this.linking.set(false);
          this.writeError.set(
            isStatus(error, 404)
              ? this.notFoundMessage(guildName, realmSlug, region)
              : syncFailureMessage(error, `Couldn't link ${guildName}. Try again.`),
          );
        },
      });
  }

  importMembers(guild: GuildServiceModel): void {
    this.begin(guild, 'importing');

    this.rosterService
      .importFromGuild(this.tenantSlug(), { guildId: guild.id })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          this.end(guild);
          this.importResults.update((results) => ({ ...results, [guild.id]: result }));
        },
        error: (error: unknown) => {
          this.end(guild);

          if (isStatus(error, 404)) {
            // Another officer unlinked it while this screen was open. Say so and drop the stale row.
            this.remove(guild);
            this.writeError.set(`This community no longer follows ${guild.name}.`);
            return;
          }

          this.writeError.set(`Couldn't import the members of ${guild.name}. Try again.`);
        },
      });
  }

  resync(guild: GuildServiceModel): void {
    this.begin(guild, 'syncing');

    this.guildService
      .resync(this.tenantSlug(), guild.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (updated) => {
          this.end(guild);
          this.upsert(updated);
          this.notice.set(`${updated.name} is up to date — ${memberCountLabel(updated.memberCount)}.`);
        },
        // The row stays as it was — stale data we still hold is shown, never blanked (degraded state).
        error: (error: unknown) => {
          this.end(guild);
          this.writeError.set(
            isStatus(error, 404)
              ? `Blizzard could not find ${guild.name} any more. It may have been renamed, moved or disbanded.`
              : syncFailureMessage(error, `Couldn't re-sync ${guild.name}. Try again.`),
          );
        },
      });
  }

  unlink(guild: GuildServiceModel): void {
    this.begin(guild, 'unlinking');

    this.guildService
      .unlink(this.tenantSlug(), guild.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.end(guild);
          this.remove(guild);
          this.notice.set(
            `Unlinked ${guild.name}. Characters already on the roster stay there until you remove them.`,
          );
        },
        error: (error: unknown) => {
          this.end(guild);

          // Already unlinked is the outcome the officer asked for.
          if (isStatus(error, 404)) {
            this.remove(guild);
            return;
          }

          this.writeError.set(`Couldn't unlink ${guild.name}. Try again.`);
        },
      });
  }

  busyWith(guild: GuildServiceModel): GuildAction | null {
    return this.busy()[guild.id] ?? null;
  }

  importResultFor(guild: GuildServiceModel): RosterImportServiceModel | null {
    return this.importResults()[guild.id] ?? null;
  }

  // The DEGRADED state, per guild: we hold this roster and it is old. Shown with its age.
  syncState(guild: GuildServiceModel): StatusPillState {
    return ageInDays(guild) >= STALE_AFTER_DAYS ? 'attention' : 'ok';
  }

  syncLabel(guild: GuildServiceModel): string {
    const days = ageInDays(guild);

    if (days >= STALE_AFTER_DAYS) {
      return `Stale ${days}d`;
    }

    return days === 0 ? 'Synced today' : `Synced ${days}d ago`;
  }

  private notFoundMessage(guildName: string, realmSlug: string, region: string): string {
    // Without credentials the gateway no-ops and the link comes back empty, which is not the officer's
    // typo — so it must not be reported as one.
    if (!this.blizzardConfigured()) {
      return 'This deployment has no Blizzard credentials, so guilds cannot be fetched from the game.';
    }

    return `Blizzard has no guild called "${guildName}" on ${realmSlug} (${region.toUpperCase()}). Check the spelling and the realm.`;
  }

  private begin(guild: GuildServiceModel, action: GuildAction): void {
    this.clearMessages();
    this.busy.update((busy) => ({ ...busy, [guild.id]: action }));
  }

  private end(guild: GuildServiceModel): void {
    this.busy.update((busy) => {
      const { [guild.id]: _done, ...rest } = busy;
      return rest;
    });
  }

  private clearMessages(): void {
    this.notice.set(null);
    this.writeError.set(null);
  }

  private upsert(guild: GuildServiceModel): void {
    this.guilds.update((guilds) =>
      guilds.some((candidate) => candidate.id === guild.id)
        ? guilds.map((candidate) => (candidate.id === guild.id ? guild : candidate))
        : [...guilds, guild],
    );
  }

  private remove(guild: GuildServiceModel): void {
    this.guilds.update((guilds) => guilds.filter((candidate) => candidate.id !== guild.id));
    this.importResults.update((results) => {
      const { [guild.id]: _gone, ...rest } = results;
      return rest;
    });
  }

  private load(tenantSlug: string, isOfficer: boolean): void {
    this.state.set('loading');
    this.clearMessages();
    this.importResults.set({});

    this.guildService
      .list(tenantSlug)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (guilds) => {
          this.guilds.set(guilds);
          this.state.set('loaded');
        },
        error: () => this.state.set('error'),
      });

    // Only an officer can link, so only an officer needs to know whether linking can work here — and
    // the overview is TenantOfficer, so a member asking would earn a 403.
    if (!isOfficer) {
      return;
    }

    this.tenantService
      .getOverview(tenantSlug)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (overview) => this.blizzardConfigured.set(overview.blizzardConfigured),
        error: () => this.blizzardConfigured.set(true),
      });
  }
}

function memberCountLabel(count: number): string {
  return count === 1 ? '1 member' : `${count} members`;
}

function ageInDays(guild: GuildServiceModel): number {
  return Math.floor((Date.now() - new Date(guild.lastSyncedAt).getTime()) / MS_PER_DAY);
}

function isStatus(error: unknown, status: number): boolean {
  return error instanceof HttpErrorResponse && error.status === status;
}

// The failures a Blizzard-backed write shares: the community's budget, the API's own throttle, and a
// validation refusal. Anything else gets the caller's generic message.
function syncFailureMessage(error: unknown, fallback: string): string {
  if (!(error instanceof HttpErrorResponse)) {
    return fallback;
  }

  if (error.status === 429) {
    const type: unknown = error.error?.type;

    if (typeof type === 'string' && type.endsWith(BUDGET_EXHAUSTED_TYPE)) {
      return `This community has used its Blizzard sync budget for now. Try again ${retryAfterLabel(error)}.`;
    }

    return 'Too many requests just now. Wait a moment and try again.';
  }

  if (error.status === 400) {
    const errors: unknown = error.error?.errors;

    if (errors !== null && typeof errors === 'object') {
      const messages = Object.values(errors as Record<string, unknown>).flatMap((value) =>
        (Array.isArray(value) ? value : [value]).map(String),
      );

      if (messages.length > 0) {
        return messages.join(' ');
      }
    }

    return 'Check the realm and guild name, then try again.';
  }

  return fallback;
}

function retryAfterLabel(error: HttpErrorResponse): string {
  const seconds = Number(error.headers?.get('Retry-After'));

  if (!Number.isFinite(seconds) || seconds <= 0) {
    return 'later';
  }

  const minutes = Math.ceil(seconds / 60);

  return minutes <= 1 ? 'in a minute' : `in ${minutes} minutes`;
}
