import { Component, DestroyRef, computed, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { CurrentUserService } from '../../../core/current-user.service';
import { MembershipService } from '../../../core/membership.service';
import {
  CreateInvitationViewModel,
  InvitationServiceModel,
  InvitationStatus,
  JoinRequestServiceModel,
  TenantMemberServiceModel,
  TenantRole,
} from '../../../models/auth.models';
import { EmptyState } from '../../../shared/empty-state/empty-state';
import { Skeleton } from '../../../shared/skeleton/skeleton';
import { StatusPill, StatusPillState } from '../../../shared/status-pill/status-pill';

type LoadState = 'loading' | 'error' | 'loaded';

// The ladder, mirroring TenantRole's own values. It exists so the two predicates below can compare
// roles the way the server does; it is not a lookup anything renders.
const ROLE_RANK: Record<TenantRole, number> = { Member: 0, Officer: 10, Owner: 20 };

const ALL_ROLES: readonly TenantRole[] = ['Member', 'Officer', 'Owner'];

// Invitations expire, and the ceiling is the point: a link that lives forever is a standing key to the
// community. The server has its own default and its own maximum — this is only what the box starts at.
const DEFAULT_EXPIRY_DAYS = 7;

/**
 * Whether `actor` may act on somebody holding `target`. Mirrors `MembershipBusiness.MayActOn` —
 * strictly above, or an Owner.
 */
export function mayActOn(actor: TenantRole, target: TenantRole): boolean {
  return ROLE_RANK[actor] > ROLE_RANK[target] || actor === 'Owner';
}

/**
 * Whether `actor` may hand out `role`. Mirrors `MembershipBusiness.MayGrant` — you cannot grant what
 * you do not hold, which is what makes Owner mintable only by an Owner.
 */
export function mayGrant(actor: TenantRole, role: TenantRole): boolean {
  return ROLE_RANK[role] <= ROLE_RANK[actor];
}

// The community's people, and the four ways that set changes: an invitation, a join request, a role
// change, a removal.
//
// Everything role-conditional on this screen HIDES and never ENFORCES. The two predicates above are
// copies of the server's, so the controls offered match the ones that will be accepted — but the
// refusals are all the server's: the TenantOfficer policies behind every write, the "strictly above"
// rule that stops an officer touching another officer, and the last-owner rule, which lives in Business
// because answering it means counting other rows. Each of those arrives here as a 403 reported inline.
//
// There is no degraded state. Nothing on this screen is Blizzard data, so there is no stale row to show
// and nothing to date-stamp — the other three states are all there are.
@Component({
  imports: [EmptyState, RouterLink, Skeleton, StatusPill],
  selector: 'scribe-member-management',
  styleUrl: './member-management.scss',
  templateUrl: './member-management.html',
})
export class MemberManagement {
  private readonly membershipService = inject(MembershipService);
  private readonly currentUser = inject(CurrentUserService);
  // Every subscription is piped through takeUntilDestroyed: these are one-shot HttpClient observables,
  // so this is about a late callback setting signals on a component the user has navigated away from.
  private readonly destroyRef = inject(DestroyRef);

  readonly tenantSlug = input.required<string>();

  readonly state = signal<LoadState>('loading');
  readonly members = signal<TenantMemberServiceModel[]>([]);
  readonly invitations = signal<InvitationServiceModel[]>([]);
  readonly joinRequests = signal<JoinRequestServiceModel[]>([]);

  readonly nextCursor = signal<string | null>(null);
  readonly loadingMore = signal(false);

  // A failed WRITE, deliberately not the same signal as `state`: `state` decides whether the screen
  // renders at all, so setting it here would take the roster of people away because one role change
  // was refused.
  readonly writeError = signal<string | null>(null);

  readonly newInvitationRole = signal<TenantRole>('Member');
  readonly newInvitationNote = signal('');
  readonly newInvitationExpiryDays = signal(DEFAULT_EXPIRY_DAYS);

  // The plaintext token, held in memory for exactly as long as this screen shows it.
  //
  // It is a bearer secret that grants membership, so it is never written to localStorage, to
  // sessionStorage, to a cookie this app sets, or to a log line — and it is never re-requested, because
  // the server kept only a hash and could not answer. Dismissing clears it for good.
  readonly mintedToken = signal<string | null>(null);

  readonly myRole = computed<TenantRole | null>(
    () =>
      this.currentUser
        .user()
        ?.memberships.find((membership) => membership.tenantSlug === this.tenantSlug())?.role ??
      null,
  );

  readonly isOfficer = computed(() => {
    const role = this.myRole();

    return role === 'Officer' || role === 'Owner';
  });

  // What this actor may hand out — the ceiling on the invitation form, the role select, and the approve
  // control alike, because all three are the same grant.
  readonly grantableRoles = computed<readonly TenantRole[]>(() => {
    const role = this.myRole();

    return role === null ? [] : ALL_ROLES.filter((candidate) => mayGrant(role, candidate));
  });

  readonly pendingRequests = computed(() =>
    this.joinRequests().filter((request) => request.status === 'Pending'),
  );

  // Live links first, spent ones after, each group newest first. An officer's question is "what is
  // outstanding"; the accepted and revoked rows are history underneath it.
  readonly orderedInvitations = computed(() =>
    [...this.invitations()].sort(
      (left, right) =>
        Number(right.status === 'Pending') - Number(left.status === 'Pending') ||
        right.createdAt.localeCompare(left.createdAt),
    ),
  );

  // The link an officer copies. Built from this app's own origin rather than configured anywhere: the
  // /join/:token route is this SPA's, and the gateway URL is the API's.
  readonly joinLink = computed(() => {
    const token = this.mintedToken();

    return token === null ? null : `${window.location.origin}/join/${token}`;
  });

  readonly copied = signal(false);

  constructor() {
    effect(() => {
      this.load(this.tenantSlug(), this.isOfficer());
    });
  }

  retry(): void {
    this.load(this.tenantSlug(), this.isOfficer());
  }

  // ---- members ----

  /** Whether the signed-in actor may change or end this person's membership. Cosmetics; see the class. */
  canActOn(member: TenantMemberServiceModel): boolean {
    const role = this.myRole();

    return (
      role !== null && mayActOn(role, member.role) && member.userId !== this.currentUser.user()?.id
    );
  }

  changeRole(member: TenantMemberServiceModel, role: string): void {
    if (!isTenantRole(role) || role === member.role) {
      return;
    }

    this.membershipService
      .setMemberRole(this.tenantSlug(), member.userId, role)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.writeError.set(null);
          this.members.update((members) =>
            members.map((candidate) =>
              candidate.userId === member.userId ? { ...candidate, role } : candidate,
            ),
          );
        },
        // By far the likeliest refusal here is the last-owner rule, so the message names it rather than
        // leaving an officer to guess why a demotion did nothing.
        error: () =>
          this.writeError.set(
            `Couldn't change ${nameOf(member)}'s role. A community must always keep at least one owner, and only an owner can demote an officer.`,
          ),
      });
  }

  remove(member: TenantMemberServiceModel): void {
    this.membershipService
      .removeMember(this.tenantSlug(), member.userId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.writeError.set(null);
          this.members.update((members) =>
            members.filter((candidate) => candidate.userId !== member.userId),
          );
        },
        error: () =>
          this.writeError.set(
            `Couldn't remove ${nameOf(member)}. A community's last owner can't be removed, and only an owner can remove an officer.`,
          ),
      });
  }

  loadMore(): void {
    const cursor = this.nextCursor();

    if (!cursor || this.loadingMore()) {
      return;
    }

    this.loadingMore.set(true);
    this.membershipService
      .listMembers(this.tenantSlug(), { cursor })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (page) => {
          this.members.update((existing) => [...existing, ...page.items]);
          this.nextCursor.set(page.hasMore ? page.nextCursor : null);
          this.loadingMore.set(false);
        },
        error: () => {
          this.loadingMore.set(false);
          this.writeError.set("Couldn't load any more members. Try again.");
        },
      });
  }

  // ---- join requests ----

  approve(request: JoinRequestServiceModel, role: string): void {
    if (!isTenantRole(role)) {
      return;
    }

    this.membershipService
      .approveJoinRequest(this.tenantSlug(), request.id, role)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.writeError.set(null);
          this.settle(request, 'Approved');
          // The new member does not appear until the list is re-read: the approve returns no content,
          // and inventing a row here would mean inventing their joined date.
          this.reloadMembers();
        },
        error: () =>
          this.writeError.set(
            `Couldn't approve ${nameOf(request)}. Another officer may have decided this one already.`,
          ),
      });
  }

  decline(request: JoinRequestServiceModel): void {
    this.membershipService
      .declineJoinRequest(this.tenantSlug(), request.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.writeError.set(null);
          this.settle(request, 'Declined');
        },
        error: () =>
          this.writeError.set(
            `Couldn't decline ${nameOf(request)}. Another officer may have decided this one already.`,
          ),
      });
  }

  // ---- invitations ----

  createInvitation(): void {
    const note = this.newInvitationNote().trim();
    const viewModel: CreateInvitationViewModel = {
      role: this.newInvitationRole(),
      note: note === '' ? null : note,
      expiresInDays: this.newInvitationExpiryDays(),
    };

    this.membershipService
      .createInvitation(this.tenantSlug(), viewModel)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (created) => {
          this.writeError.set(null);
          this.copied.set(false);
          this.mintedToken.set(created.token);
          this.invitations.update((invitations) => [created.invitation, ...invitations]);
          this.newInvitationNote.set('');
        },
        error: () => this.writeError.set("Couldn't create that invitation. Try again."),
      });
  }

  async copyJoinLink(): Promise<void> {
    const link = this.joinLink();

    if (link === null) {
      return;
    }

    try {
      await navigator.clipboard.writeText(link);
      this.copied.set(true);
    } catch {
      // Clipboard access is denied in plenty of ordinary situations, and the link is on screen in a
      // selectable field regardless — so this is a non-event, not an error worth a banner.
      this.copied.set(false);
    }
  }

  /** Forgets the plaintext token. It is unrecoverable afterwards, which is the design. */
  dismissToken(): void {
    this.mintedToken.set(null);
    this.copied.set(false);
  }

  revokeInvitation(invitation: InvitationServiceModel): void {
    this.membershipService
      .revokeInvitation(this.tenantSlug(), invitation.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.writeError.set(null);
          this.invitations.update((invitations) =>
            invitations.map((candidate) =>
              candidate.id === invitation.id
                ? { ...candidate, status: 'Revoked' as InvitationStatus }
                : candidate,
            ),
          );

          // If the link being killed is the one still on screen, take it off the screen too.
          if (this.mintedToken() !== null && this.invitations()[0]?.id === invitation.id) {
            this.dismissToken();
          }
        },
        error: () => this.writeError.set("Couldn't revoke that invitation. Try again."),
      });
  }

  // ---- display helpers ----

  /** A member or applicant's name, or a plain stand-in. Never their email — that is not ours to show. */
  displayNameOf(person: { displayName: string | null }): string {
    return nameOf(person);
  }

  /** Date only. An exact ISO day is unambiguous everywhere; a join date is not an appointment. */
  asDate(instant: string): string {
    return instant.slice(0, 10);
  }

  statusStateOf(status: InvitationStatus): StatusPillState {
    switch (status) {
      case 'Pending':
        return 'ok';
      case 'Expired':
        return 'attention';
      default:
        // Accepted and revoked are both settled and neither is a problem — neutral, and the word beside
        // the dot is what tells them apart.
        return 'neutral';
    }
  }

  // ---- loading ----

  private settle(request: JoinRequestServiceModel, status: 'Approved' | 'Declined'): void {
    this.joinRequests.update((requests) =>
      requests.map((candidate) =>
        candidate.id === request.id
          ? { ...candidate, status, decidedAt: new Date().toISOString() }
          : candidate,
      ),
    );
  }

  private reloadMembers(): void {
    this.membershipService
      .listMembers(this.tenantSlug())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (page) => {
          this.members.set(page.items);
          this.nextCursor.set(page.hasMore ? page.nextCursor : null);
        },
        error: () => this.writeError.set("Couldn't refresh the member list."),
      });
  }

  private load(tenantSlug: string, isOfficer: boolean): void {
    this.state.set('loading');
    this.writeError.set(null);
    this.dismissToken();

    this.membershipService
      .listMembers(tenantSlug)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (page) => {
          this.members.set(page.items);
          this.nextCursor.set(page.hasMore ? page.nextCursor : null);
          this.state.set('loaded');
        },
        error: () => this.state.set('error'),
      });

    // Both of these are TenantOfficer server-side, so a plain member is not asked for them at all —
    // the panels they feed are not rendered for one either. Hiding the controls is about not offering
    // what will be refused; the policies are what actually refuse it.
    if (!isOfficer) {
      this.invitations.set([]);
      this.joinRequests.set([]);

      return;
    }

    this.membershipService
      .listInvitations(tenantSlug)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        // A community with no outstanding links is an empty panel, not a failed screen, and so is one
        // whose invitation list we briefly could not read — neither touches `state`.
        next: (invitations) => this.invitations.set(invitations),
        error: () => this.invitations.set([]),
      });

    // Pending only. The queue is a thing to work through; decided requests are not part of the job.
    this.membershipService
      .listJoinRequests(tenantSlug, 'Pending')
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (requests) => this.joinRequests.set(requests),
        error: () => this.joinRequests.set([]),
      });
  }
}

function isTenantRole(value: string): value is TenantRole {
  return (ALL_ROLES as readonly string[]).includes(value);
}

function nameOf(person: { displayName: string | null }): string {
  return person.displayName ?? 'Unnamed member';
}
