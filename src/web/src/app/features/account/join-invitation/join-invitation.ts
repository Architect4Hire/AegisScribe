import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../../core/auth.service';
import { CurrentUserService } from '../../../core/current-user.service';
import { InvitationService } from '../../../core/invitation.service';
import {
  InvitationPreviewServiceModel,
  InvitationRefusal,
  TenantRole,
} from '../../../models/auth.models';
import { EmptyState } from '../../../shared/empty-state/empty-state';
import { Skeleton } from '../../../shared/skeleton/skeleton';

// 'loading'  — asking what the token is worth.
// 'live'     — a real invitation; the button differs by whether anyone is signed in.
// 'accepting'— spending it.
// 'refused'  — expired, consumed, revoked, unknown, or the API is down. Names no community.
type ScreenState = 'loading' | 'live' | 'accepting' | 'refused';

// What each refusal should say. Deliberately four different answers, because what the holder should
// do next differs — and not one of them names the community, because a dead token no longer entitles
// its holder to know what it was for.
const REFUSALS: Record<InvitationRefusal, { heading: string; body: string }> = {
  expired: {
    heading: 'This invitation has expired',
    body: 'Invitation links are short-lived. Ask whoever sent it for a fresh one.',
  },
  consumed: {
    heading: 'This invitation has already been used',
    body: 'Each link works once. If you did not use it, ask whoever sent it for a new one.',
  },
  revoked: {
    heading: 'This invitation was withdrawn',
    body: 'Someone in the community cancelled this link. Get in touch with them directly.',
  },
  'not-found': {
    heading: 'This link is not valid',
    body: 'Check that you copied the whole address, including the part after the last slash.',
  },
  unavailable: {
    heading: 'We could not check this invitation',
    body: 'Something went wrong on our side rather than with your link. Try again in a moment.',
  },
};

// The invitee's half of the membership lifecycle (8.3b).
//
// Unguarded on purpose: it must render for somebody with no session, because the whole point is to
// show what they were invited to BEFORE asking them to sign in. Arriving signed out, the route
// survives register-and-sign-in and returns to itself, carrying the token in the returnUrl.
//
// The token is a bearer secret. It is read from the route, held in a field while the screen is open,
// and written nowhere: no localStorage, no sessionStorage, no cookie this app sets, no log line. On
// success the navigation REPLACES this URL so the spent token does not sit in the back button.
//
// No S-numbered mockup exists for this screen in design/aegisscribe-armory.html — it is composed from
// §05's primitives and §07's tenant context, at the same measure as the other account screens.
@Component({
  imports: [EmptyState, Skeleton],
  selector: 'scribe-join-invitation',
  styleUrl: './join-invitation.scss',
  templateUrl: './join-invitation.html',
})
export class JoinInvitation implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly currentUser = inject(CurrentUserService);
  private readonly invitations = inject(InvitationService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  private token = '';

  readonly state = signal<ScreenState>('loading');
  readonly preview = signal<InvitationPreviewServiceModel | null>(null);
  readonly refusal = signal<InvitationRefusal>('unavailable');
  readonly signedIn = signal(false);

  readonly refusalCopy = () => REFUSALS[this.refusal()];

  async ngOnInit(): Promise<void> {
    this.token = this.route.snapshot.paramMap.get('token') ?? '';

    // Both, in parallel: whether the link is worth anything, and whether we already know who this is.
    // probeSession never redirects, so a signed-out visitor stays here and sees the invitation.
    const [user] = await Promise.all([this.currentUser.probeSession(), this.load()]);

    this.signedIn.set(user !== null);
  }

  // The signed-out path. The token rides in the returnUrl so the gateway lands the browser back on
  // this exact route after sign-in, where the component runs again and finds a session.
  signIn(): void {
    this.auth.login(`/join/${encodeURIComponent(this.token)}`);
  }

  // Only 'unavailable' is worth retrying, and the template offers the control only for that one. The
  // other four are settled facts about the token — retrying a revoked link cannot un-revoke it, and a
  // button that re-runs a request guaranteed to fail the same way is worse than no button.
  async retry(): Promise<void> {
    this.state.set('loading');

    await this.load();
  }

  async accept(): Promise<void> {
    this.state.set('accepting');

    try {
      const accepted = await firstValueFrom(this.invitations.accept(this.token));

      // replaceUrl: the token is spent, and leaving it in the history stack means the back button
      // re-renders a screen holding a dead secret.
      await this.router.navigate(['/t', accepted.tenantSlug], { replaceUrl: true });
    } catch (error) {
      this.refusal.set(error as InvitationRefusal);
      this.state.set('refused');
    }
  }

  // Role names come from the server as strings (api-contract.md), and this build must tolerate one it
  // has never heard of rather than rendering nothing.
  describeRole(role: TenantRole | string): string {
    switch (role) {
      case 'Owner':
        return 'an owner';
      case 'Officer':
        return 'an officer';
      case 'Member':
        return 'a member';
      default:
        return 'a member';
    }
  }

  private async load(): Promise<void> {
    if (this.token.length === 0) {
      this.refusal.set('not-found');
      this.state.set('refused');
      return;
    }

    try {
      this.preview.set(await firstValueFrom(this.invitations.preview(this.token)));
      this.state.set('live');
    } catch (error) {
      this.refusal.set(error as InvitationRefusal);
      this.state.set('refused');
    }
  }
}
