import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';
import { AuthService } from '../../../core/auth.service';
import { CurrentUserService } from '../../../core/current-user.service';
import { InvitationService } from '../../../core/invitation.service';
import {
  InvitationAcceptedServiceModel,
  InvitationPreviewServiceModel,
  UserServiceModel,
} from '../../../models/auth.models';
import { JoinInvitation } from './join-invitation';

const TOKEN = 'a-token-that-is-a-bearer-secret';

const livePreview: InvitationPreviewServiceModel = {
  tenantSlug: 'ashes-of-dawn',
  tenantName: 'Ashes of Dawn',
  role: 'Officer',
  expiresAt: '2026-10-01T00:00:00+00:00',
};

const accepted: InvitationAcceptedServiceModel = {
  tenantSlug: 'ashes-of-dawn',
  tenantName: 'Ashes of Dawn',
  role: 'Officer',
  alreadyAMember: false,
};

const signedInUser = { id: 'u1', email: 'a@b.c' } as UserServiceModel;

describe('JoinInvitation', () => {
  let fixture: ComponentFixture<JoinInvitation>;
  let component: JoinInvitation;

  let preview: ReturnType<typeof vi.fn<() => Observable<InvitationPreviewServiceModel>>>;
  let accept: ReturnType<typeof vi.fn<() => Observable<InvitationAcceptedServiceModel>>>;
  let probeSession: ReturnType<typeof vi.fn<() => Promise<UserServiceModel | null>>>;
  let clear: ReturnType<typeof vi.fn<() => void>>;
  let login: ReturnType<typeof vi.fn<(returnUrl: string) => void>>;
  let navigate: ReturnType<
    typeof vi.fn<(commands: readonly unknown[], extras?: unknown) => Promise<boolean>>
  >;

  beforeEach(async () => {
    preview = vi.fn(() => of(livePreview));
    accept = vi.fn(() => of(accepted));
    probeSession = vi.fn(async () => null);
    clear = vi.fn();
    login = vi.fn();

    await TestBed.configureTestingModule({
      imports: [JoinInvitation],
      providers: [
        provideRouter([]),
        { provide: InvitationService, useValue: { preview, accept } },
        { provide: CurrentUserService, useValue: { probeSession, clear } },
        { provide: AuthService, useValue: { login } },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: new Map([['token', TOKEN]]) } },
        },
      ],
    }).compileComponents();

    navigate = vi.fn(async () => true);
    vi.spyOn(TestBed.inject(Router), 'navigate').mockImplementation(
      navigate as unknown as Router['navigate'],
    );
  });

  // The second detectChanges is load-bearing: ngOnInit resolves during whenStable, so without it the
  // DOM is still the loading state and every assertion about rendered text passes vacuously.
  async function render(): Promise<void> {
    fixture = TestBed.createComponent(JoinInvitation);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('renders the community and role to somebody who is signed out', async () => {
    // The whole reason the preview is anonymous and this route is unguarded: you should see what you
    // were invited to BEFORE being asked to sign in, not after.
    await render();

    expect(component.state()).toBe('live');
    expect(component.signedIn()).toBe(false);
    expect(component.preview()?.tenantName).toBe('Ashes of Dawn');
    expect(login).not.toHaveBeenCalled();
  });

  it('sends the token through sign-in in the returnUrl so the route returns to itself', async () => {
    await render();

    component.signIn();

    // The round-trip. The gateway validates this as a local path and lands the browser back here,
    // where the component runs again and finds a session.
    expect(login).toHaveBeenCalledWith(`/join/${encodeURIComponent(TOKEN)}`);
  });

  it('never writes the token to browser storage or a cookie', async () => {
    // The restriction, asserted rather than asserted-in-a-comment. The token lives in the route and
    // in a field; anything that persists it outlives the tab and the sign-in round-trip.
    await render();
    component.signIn();

    expect(localStorage.getItem('token')).toBeNull();
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
    expect(document.cookie).not.toContain(TOKEN);
  });

  it('accepts when signed in and replaces the URL so the spent token leaves the history', async () => {
    probeSession.mockResolvedValue(signedInUser);

    await render();
    expect(component.signedIn()).toBe(true);

    await component.accept();

    expect(accept).toHaveBeenCalledWith(TOKEN);

    // replaceUrl: the back button must not re-render a screen holding a dead secret.
    expect(navigate).toHaveBeenCalledWith(['/t', 'ashes-of-dawn'], { replaceUrl: true });
  });

  it('drops the cached memberships before navigating, so the tenant guard sees the new one', async () => {
    probeSession.mockResolvedValue(signedInUser);

    await render();
    await component.accept();

    expect(clear).toHaveBeenCalledTimes(1);
    expect(clear.mock.invocationCallOrder[0]).toBeLessThan(navigate.mock.invocationCallOrder[0]);
  });

  it.each([
    ['expired', 'This invitation has expired'],
    ['consumed', 'This invitation has already been used'],
    ['revoked', 'This invitation was withdrawn'],
    ['not-found', 'This link is not valid'],
  ])('tells %s apart from the others without naming the community', async (refusal, heading) => {
    preview.mockReturnValue(throwError(() => refusal));

    await render();

    expect(component.state()).toBe('refused');
    expect(component.refusalCopy().heading).toBe(heading);
    expect(component.preview()).toBeNull();

    // A dead token buys its holder nothing, including the knowledge of what it was for.
    const rendered = fixture.nativeElement.textContent as string;
    expect(rendered).not.toContain('Ashes of Dawn');
    expect(rendered).not.toContain('ashes-of-dawn');
  });

  it('offers a retry for a transient failure and re-asks the server', async () => {
    preview.mockReturnValueOnce(throwError(() => 'unavailable'));

    await render();
    expect(component.state()).toBe('refused');
    expect(fixture.nativeElement.textContent).toContain('Try again');

    preview.mockReturnValue(of(livePreview));
    await component.retry();

    expect(preview).toHaveBeenCalledTimes(2);
    expect(component.state()).toBe('live');
  });

  it.each(['expired', 'consumed', 'revoked', 'not-found'])(
    'offers no retry for %s, because retrying cannot fix a dead link',
    async (refusal) => {
      preview.mockReturnValue(throwError(() => refusal));

      await render();

      expect(component.state()).toBe('refused');
      expect(fixture.nativeElement.textContent).not.toContain('Try again');
    },
  );

  it('shows a refusal rather than a half-joined screen when the accept loses the race', async () => {
    probeSession.mockResolvedValue(signedInUser);
    accept.mockReturnValue(throwError(() => 'consumed'));

    await render();
    await component.accept();

    expect(component.state()).toBe('refused');
    expect(component.refusal()).toBe('consumed');
    expect(navigate).not.toHaveBeenCalled();
    expect(clear).not.toHaveBeenCalled();
  });
});
