import { Component, DestroyRef, computed, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { CurrentUserService } from '../../../core/current-user.service';
import { TenantService } from '../../../core/tenant.service';
import { TenantOverviewServiceModel } from '../../../models/auth.models';
import { Skeleton } from '../../../shared/skeleton/skeleton';

/** One line of the checklist. Derived on every render; never stored anywhere. */
export interface ChecklistStep {
  id: string;
  label: string;
  body: string;
  done: boolean;
  /** Router path this step leads to. Always a real destination — no step is a dead end. */
  link: string[];
  /** A panel on the destination to scroll to — for a step whose screen is the one already showing. */
  fragment?: string;
  linkLabel: string;
  /**
   * Set when the step can be started but this deployment cannot finish it the usual way. Renders as a
   * plain note rather than a disabled control, because the step still has a manual path.
   */
  caveat?: string;
}

// Per-viewer, per-community. localStorage rather than a server column on purpose: dismissing a
// checklist is a preference about a screen, not a fact about the community, and storing it server-side
// would mean one officer's "hide this" hid it from everybody.
const DISMISSED_KEY_PREFIX = 'aegisscribe:first-run-dismissed:';

// The id scribe-add-character puts on its panel. The checklist renders on the roster, so a plain link to
// the roster would go nowhere; the fragment scrolls to the form instead.
const ADD_CHARACTER_ANCHOR = 'add-character';

// "You just created this — here's what's next."
//
// The whole panel is a pure function of TenantOverviewServiceModel: five facts about what the community
// HAS, turned into four steps. There is no step counter, no progress column, and there must never be
// one — a persisted "step 3 of 4" starts lying the first time an officer deletes a rank, and then goes
// on lying forever. Delete the rank, the step un-completes. That is the design.
//
// It is a CHECKLIST, not a wizard: every step links into navigation that already exists, nothing is
// disabled because an earlier step is unfinished, and the steps can be done in any order.
@Component({
  imports: [RouterLink, Skeleton],
  selector: 'scribe-first-run-checklist',
  styleUrl: './first-run-checklist.scss',
  templateUrl: './first-run-checklist.html',
})
export class FirstRunChecklist {
  private readonly tenantService = inject(TenantService);
  private readonly currentUser = inject(CurrentUserService);
  private readonly destroyRef = inject(DestroyRef);

  readonly tenantSlug = input.required<string>();

  readonly overview = signal<TenantOverviewServiceModel | null>(null);
  readonly loading = signal(false);
  readonly dismissed = signal(false);

  // Owner/Officer only. A plain member landing in a fresh community sees their screens' ordinary empty
  // states, not somebody else's setup list — and the endpoint behind this is TenantOfficer anyway, so
  // asking would earn a 403. Same idiom as roster-table's own officer check.
  readonly isOfficer = computed(() => {
    const role = this.currentUser
      .user()
      ?.memberships.find((membership) => membership.tenantSlug === this.tenantSlug())?.role;

    return role === 'Officer' || role === 'Owner';
  });

  readonly steps = computed<ChecklistStep[]>(() => {
    const overview = this.overview();

    if (overview === null) {
      return [];
    }

    const base = ['/t', this.tenantSlug()];

    // An array rather than four blocks of markup, which is the seam Discord (10.4) slots into when it
    // exists. Nothing fakes it in the meantime: a greyed-out "connect Discord — coming soon" row would
    // be a step nobody can take, which is exactly what this checklist is not for.
    return [
      {
        id: 'guild',
        label: 'Link your guild',
        body: 'Point this community at its guild in game, and AegisScribe keeps its roster and ranks in step.',
        done: overview.hasLinkedGuild,
        link: [...base, 'guilds'],
        linkLabel: 'Link a guild',
        // Credentials are optional by design and the gateway no-ops without them (CLAUDE.md → Usage),
        // so this says so plainly instead of letting an officer type a guild name and hit a failure.
        // It stays a live step rather than a disabled one, because the manual path below still works.
        caveat: overview.blizzardConfigured
          ? undefined
          : 'This deployment has no Blizzard credentials, so guilds cannot be imported from the game. You can still add characters to the roster by hand.',
      },
      {
        id: 'roster',
        label: 'Build your roster',
        body: 'Import the members of a linked guild, or add characters one at a time.',
        done: overview.rosterCount > 0,
        // Two ways onto the roster, and the step leads to whichever this community can use: importing
        // lives beside the linked guild, adding by hand is on the roster itself (the panel opens on its
        // own there while the roster is empty).
        link: overview.hasLinkedGuild ? [...base, 'guilds'] : [...base, 'roster'],
        fragment: overview.hasLinkedGuild ? undefined : ADD_CHARACTER_ANCHOR,
        linkLabel: overview.hasLinkedGuild ? 'Import members' : 'Add a character',
      },
      {
        id: 'ranks',
        label: 'Name your ranks',
        body: "Your community's own ladder — Raider, Trial, Social. Unrelated to the ranks the guild uses in game.",
        done: overview.rankCount > 0,
        link: [...base, 'ranks'],
        linkLabel: 'Set up ranks',
      },
      {
        id: 'members',
        label: 'Invite people',
        body: 'Create an invitation link and pass it on. Anyone who accepts joins at the role you chose.',
        // More than just you. A brand-new community counts 1, which is not "you have members".
        done: overview.memberCount > 1,
        link: [...base, 'members'],
        linkLabel: 'Invite somebody',
      },
    ];
  });

  readonly remaining = computed(() => this.steps().filter((step) => !step.done).length);

  readonly visible = computed(
    () =>
      this.isOfficer() &&
      !this.dismissed() &&
      // Once every step is done this has served its purpose and goes away on its own — a permanent
      // all-ticked panel is just clutter on a working community's screen.
      (this.loading() || this.remaining() > 0),
  );

  constructor() {
    effect(() => {
      this.load(this.tenantSlug(), this.isOfficer());
    });
  }

  dismiss(): void {
    this.dismissed.set(true);
    writeDismissed(this.tenantSlug());
  }

  private load(tenantSlug: string, isOfficer: boolean): void {
    // Re-read per community, so dismissing one community's checklist never hides another's.
    //
    // Held in a local and never read back off the signal, which is load-bearing rather than tidiness:
    // this runs inside an effect, so reading `dismissed()` here would make the effect DEPEND on a
    // signal it also writes — and dismiss() setting it would re-run this method, which would re-read
    // storage and undo the dismissal. That is invisible while localStorage works (the value just
    // written reads back as true) and breaks the moment it throws.
    const dismissed = readDismissed(tenantSlug);
    this.dismissed.set(dismissed);
    this.overview.set(null);

    if (!isOfficer || dismissed) {
      return;
    }

    this.loading.set(true);
    this.tenantService
      .getOverview(tenantSlug)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (overview) => {
          this.overview.set(overview);
          this.loading.set(false);
        },
        // No error state, deliberately. This panel is an aside above somebody's roster; if we cannot
        // say what is left to set up, saying nothing is better than putting an error banner over a
        // screen that is working fine. The roster below owns its own error state.
        error: () => {
          this.overview.set(null);
          this.loading.set(false);
        },
      });
  }
}

// Both wrapped, because localStorage throws rather than returning null in a private window or with
// site data blocked — and a checklist must never be the reason a roster fails to render.
function readDismissed(tenantSlug: string): boolean {
  try {
    return localStorage.getItem(DISMISSED_KEY_PREFIX + tenantSlug) === 'true';
  } catch {
    return false;
  }
}

function writeDismissed(tenantSlug: string): void {
  try {
    localStorage.setItem(DISMISSED_KEY_PREFIX + tenantSlug, 'true');
  } catch {
    // The panel still hides for this visit; it simply comes back next time. An acceptable degradation
    // for a preference, and far better than throwing.
  }
}
