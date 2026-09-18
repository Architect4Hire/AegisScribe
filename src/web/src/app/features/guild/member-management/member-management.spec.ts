import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';
import { CurrentUserService } from '../../../core/current-user.service';
import { MembershipService } from '../../../core/membership.service';
import {
  CreatedInvitationServiceModel,
  InvitationServiceModel,
  JoinRequestServiceModel,
  TenantMemberServiceModel,
  TenantRole,
  UserServiceModel,
} from '../../../models/auth.models';
import { CursorPageServiceModel } from '../../../models/roster.models';
import { MemberManagement, mayActOn, mayGrant } from './member-management';

const SLUG = 'emberfall';

function member(over: Partial<TenantMemberServiceModel> = {}): TenantMemberServiceModel {
  return {
    userId: 'u-member',
    displayName: 'Rin',
    role: 'Member',
    joinedAt: '2026-04-01T09:00:00Z',
    claimedCharacters: [],
    ...over,
  };
}

function page(
  items: TenantMemberServiceModel[],
  over: Partial<CursorPageServiceModel<TenantMemberServiceModel>> = {},
): CursorPageServiceModel<TenantMemberServiceModel> {
  return { items, nextCursor: null, hasMore: false, ...over };
}

function invitation(over: Partial<InvitationServiceModel> = {}): InvitationServiceModel {
  return {
    id: 'i-1',
    role: 'Member',
    note: 'For Thal',
    createdAt: '2026-09-01T09:00:00Z',
    expiresAt: '2026-09-08T09:00:00Z',
    status: 'Pending',
    ...over,
  };
}

function joinRequest(over: Partial<JoinRequestServiceModel> = {}): JoinRequestServiceModel {
  return {
    id: 'j-1',
    userId: 'u-applicant',
    displayName: 'Sable',
    message: 'Raided with half of you in Legion.',
    status: 'Pending',
    requestedAt: '2026-09-10T18:00:00Z',
    decidedAt: null,
    ...over,
  };
}

function signedInAs(role: TenantRole, userId = 'u-me'): UserServiceModel {
  return {
    id: userId,
    email: 'me@example.test',
    displayName: 'Me',
    createdAt: '2026-01-01T00:00:00Z',
    memberships: [
      {
        tenantId: 't-1',
        tenantSlug: SLUG,
        tenantName: 'Emberfall',
        role,
        joinedAt: '2026-01-01T00:00:00Z',
      },
    ],
  };
}

interface Harness {
  fixture: ComponentFixture<MemberManagement>;
  membership: {
    listMembers: ReturnType<typeof vi.fn>;
    setMemberRole: ReturnType<typeof vi.fn>;
    removeMember: ReturnType<typeof vi.fn>;
    listInvitations: ReturnType<typeof vi.fn>;
    createInvitation: ReturnType<typeof vi.fn>;
    revokeInvitation: ReturnType<typeof vi.fn>;
    listJoinRequests: ReturnType<typeof vi.fn>;
    approveJoinRequest: ReturnType<typeof vi.fn>;
    declineJoinRequest: ReturnType<typeof vi.fn>;
  };
  host: HTMLElement;
}

async function createWith(
  options: {
    role?: TenantRole;
    members?: () => Observable<CursorPageServiceModel<TenantMemberServiceModel>>;
    invitations?: () => Observable<InvitationServiceModel[]>;
    joinRequests?: () => Observable<JoinRequestServiceModel[]>;
    created?: () => Observable<CreatedInvitationServiceModel>;
  } = {},
): Promise<Harness> {
  const membership = {
    listMembers: vi.fn(options.members ?? (() => of(page([member()])))),
    setMemberRole: vi.fn(() => of(void 0)),
    removeMember: vi.fn(() => of(void 0)),
    listInvitations: vi.fn(options.invitations ?? (() => of([]))),
    createInvitation: vi.fn(
      options.created ?? (() => of({ invitation: invitation(), token: 'tok-abc123' })),
    ),
    revokeInvitation: vi.fn(() => of(void 0)),
    listJoinRequests: vi.fn(options.joinRequests ?? (() => of([]))),
    approveJoinRequest: vi.fn(() => of(void 0)),
    declineJoinRequest: vi.fn(() => of(void 0)),
  };

  // Reset first, because two of the tests below build the same screen twice in one case — as an
  // officer and then as an owner — and the comparison is the assertion.
  TestBed.resetTestingModule();

  await TestBed.configureTestingModule({
    imports: [MemberManagement],
    providers: [
      provideRouter([]),
      { provide: MembershipService, useValue: membership },
      {
        provide: CurrentUserService,
        useValue: { user: () => signedInAs(options.role ?? 'Officer') },
      },
    ],
  }).compileComponents();

  const fixture = TestBed.createComponent(MemberManagement);
  fixture.componentRef.setInput('tenantSlug', SLUG);
  fixture.detectChanges();

  return { fixture, membership, host: fixture.nativeElement as HTMLElement };
}

describe('MemberManagement', () => {
  it("shows each member's claimed characters, and names the ones who have claimed none", async () => {
    // The line 8.4 exists for: an officer's question is which of their people have not said which
    // character is theirs, and that is only answerable with both halves on the same row.
    const { host } = await createWith({
      members: () =>
        of(
          page([
            member({
              userId: 'u-claimed',
              displayName: 'Rin',
              claimedCharacters: [
                {
                  characterId: 'c-1',
                  name: 'Thalyra',
                  realmSlug: 'argent-dawn',
                  region: 'eu',
                  class: 'Evoker',
                  classColor: '#33937f',
                },
              ],
            }),
            member({ userId: 'u-bare', displayName: 'Corvin', claimedCharacters: [] }),
          ]),
        ),
    });

    expect(host.textContent).toContain('Thalyra');
    expect(host.textContent).toContain('No character claimed');

    // The class colour is set once, as a custom property on the chip, and inherited by the name — no
    // class→hex table in TypeScript and no per-class CSS class.
    const chip = host.querySelector<HTMLElement>('.mm-claim');
    expect(chip?.style.getPropertyValue('--class-color')).toBe('#33937f');

    // And it links to the character, rather than being dead text.
    expect(chip?.getAttribute('href')).toBe(`/t/${SLUG}/characters/eu/argent-dawn/Thalyra`);
  });

  it('offers an officer no way to mint an owner, and an owner one', async () => {
    // mayGrant, mirrored: you cannot hand out what you do not hold. Hiding it is cosmetics — the
    // server refuses it either way — but offering a control that always fails is its own defect.
    const asOfficer = await createWith({ role: 'Officer' });
    const officerOptions = Array.from(
      asOfficer.host.querySelectorAll<HTMLOptionElement>('#invite-role option'),
    ).map((option) => option.value);
    expect(officerOptions).toEqual(['Member', 'Officer']);

    const asOwner = await createWith({ role: 'Owner' });
    const ownerOptions = Array.from(
      asOwner.host.querySelectorAll<HTMLOptionElement>('#invite-role option'),
    ).map((option) => option.value);
    expect(ownerOptions).toEqual(['Member', 'Officer', 'Owner']);
  });

  it('gives an officer no controls over another officer, and an owner all of them', async () => {
    // mayActOn, mirrored: strictly above, or an Owner.
    const members = () =>
      of(page([member({ userId: 'u-officer', displayName: 'Bram', role: 'Officer' })]));

    const asOfficer = await createWith({ role: 'Officer', members });
    expect(asOfficer.host.querySelector('.mm-member .btn-danger')).toBeNull();
    // Their role is still shown — who holds what is not a secret inside a community.
    expect(asOfficer.host.textContent).toContain('Officer');

    const asOwner = await createWith({ role: 'Owner', members });
    expect(asOwner.host.querySelector('.mm-member .btn-danger')).not.toBeNull();
  });

  it('never offers the signed-in officer a control over themselves', async () => {
    // Leaving is its own act on its own route; an officer removing themselves from the member list is
    // not what that button is for, and the last-owner rule would be the only thing between an owner
    // and deleting their own community's ownership by mis-click.
    const { host } = await createWith({
      role: 'Owner',
      members: () => of(page([member({ userId: 'u-me', displayName: 'Me', role: 'Owner' })])),
    });

    expect(host.querySelector('.mm-member .btn-danger')).toBeNull();
  });

  it('asks for neither invitations nor join requests as a plain member, and shows neither panel', async () => {
    // Both endpoints are TenantOfficer, so a member's calls would 403. Not making them is the point:
    // the panels are hidden because there is nothing to put in them, not to conceal anything.
    const { host, membership } = await createWith({ role: 'Member' });

    expect(membership.listInvitations).not.toHaveBeenCalled();
    expect(membership.listJoinRequests).not.toHaveBeenCalled();
    expect(host.textContent).not.toContain('Invitations');
    expect(host.textContent).not.toContain('Requests to join');

    // They still see who is in their community — the member list is TenantMember.
    expect(membership.listMembers).toHaveBeenCalled();
    expect(host.textContent).toContain('People');
  });

  it('shows a new invitation link once, and never asks for it again', async () => {
    const { fixture, host, membership } = await createWith({ role: 'Officer' });

    host.querySelectorAll<HTMLButtonElement>('button').forEach((button) => {
      if (button.textContent?.includes('Create invitation')) {
        button.click();
      }
    });
    fixture.detectChanges();

    const link = host.querySelector<HTMLInputElement>('.mm-token-link');
    expect(link?.value).toContain('/join/tok-abc123');
    expect(host.textContent).toContain('shown once');

    // Dismissing forgets it. The server kept only a hash, so nothing can produce it a second time.
    host.querySelectorAll<HTMLButtonElement>('.mm-token-row button').forEach((button) => {
      if (button.textContent?.trim() === 'Done') {
        button.click();
      }
    });
    fixture.detectChanges();

    expect(host.querySelector('.mm-token-link')).toBeNull();
    expect(membership.createInvitation).toHaveBeenCalledTimes(1);
  });

  it('renders an applicant’s own words as text, and offers approve and decline', async () => {
    const { host, membership } = await createWith({
      role: 'Officer',
      joinRequests: () => of([joinRequest({ message: '<b>not markup</b>' })]),
    });

    expect(membership.listJoinRequests).toHaveBeenCalledWith(SLUG, 'Pending');

    const message = host.querySelector('.mm-message');
    expect(message?.textContent).toContain('<b>not markup</b>');
    expect(message?.querySelector('b')).toBeNull();

    expect(host.textContent).toContain('Approve');
    expect(host.textContent).toContain('Decline');
  });

  it('dresses every destructive control as an outline, never as the primary action', async () => {
    // "Destructive is outlined, never filled — filled red invites the mis-click it warns about."
    const { host } = await createWith({
      role: 'Owner',
      members: () => of(page([member({ userId: 'u-other', role: 'Member' })])),
      invitations: () => of([invitation()]),
      joinRequests: () => of([joinRequest()]),
    });

    const destructive = ['Remove', 'Revoke', 'Decline'];
    const buttons = Array.from(host.querySelectorAll<HTMLButtonElement>('button'));

    for (const label of destructive) {
      const button = buttons.find((candidate) => candidate.textContent?.trim() === label);
      expect(button, `expected a ${label} button`).toBeDefined();
      expect(button?.classList.contains('btn-danger')).toBe(true);
      expect(button?.classList.contains('btn-primary')).toBe(false);
    }
  });

  it('reports a refused role change without taking the screen away', async () => {
    // The last-owner rule and the "strictly above" rule both arrive as 403s. A write failing must not
    // wipe the list of people the officer is working through.
    const { fixture, host, membership } = await createWith({
      role: 'Owner',
      members: () => of(page([member({ userId: 'u-other', displayName: 'Rin' })])),
    });
    membership.setMemberRole.mockReturnValue(throwError(() => new Error('403')));

    const select = host.querySelector<HTMLSelectElement>('.mm-member select');
    select!.value = 'Officer';
    select!.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(host.querySelector('[role="alert"]')?.textContent).toContain('at least one owner');
    expect(host.textContent).toContain('Rin');
  });

  it('offers a retry when the member list itself fails', async () => {
    const { host } = await createWith({ members: () => throwError(() => new Error('down')) });

    expect(host.textContent).toContain("Couldn't load this community's members");
    expect(host.querySelector('button')?.textContent).toContain('Try again');
  });
});

describe('the mirrored membership predicates', () => {
  // Copies of MembershipBusiness.MayActOn / MayGrant. They decide what the UI offers, never what is
  // allowed — but if they drift from the server's, the screen starts offering refusals.
  it('lets an actor act strictly above them, or anywhere as an owner', () => {
    expect(mayActOn('Officer', 'Member')).toBe(true);
    expect(mayActOn('Officer', 'Officer')).toBe(false);
    expect(mayActOn('Officer', 'Owner')).toBe(false);
    expect(mayActOn('Owner', 'Owner')).toBe(true);
    expect(mayActOn('Member', 'Member')).toBe(false);
  });

  it('lets nobody hand out what they do not hold', () => {
    expect(mayGrant('Officer', 'Member')).toBe(true);
    expect(mayGrant('Officer', 'Officer')).toBe(true);
    expect(mayGrant('Officer', 'Owner')).toBe(false);
    expect(mayGrant('Owner', 'Owner')).toBe(true);
  });
});
