import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';
import { CurrentUserService } from '../../../core/current-user.service';
import { TenantService } from '../../../core/tenant.service';
import {
  TenantOverviewServiceModel,
  TenantRole,
  UserServiceModel,
} from '../../../models/auth.models';
import { FirstRunChecklist } from './first-run-checklist';

const FRESH: TenantOverviewServiceModel = {
  hasLinkedGuild: false,
  rankCount: 0,
  rosterCount: 0,
  memberCount: 1,
  blizzardConfigured: true,
};

function overview(over: Partial<TenantOverviewServiceModel> = {}): TenantOverviewServiceModel {
  return { ...FRESH, ...over };
}

function signedInAs(role: TenantRole, slugs: string[] = ['emberfall']): UserServiceModel {
  return {
    id: 'u-me',
    email: 'me@example.test',
    displayName: 'Me',
    createdAt: '2026-01-01T00:00:00Z',
    memberships: slugs.map((slug, index) => ({
      tenantId: `t-${index}`,
      tenantSlug: slug,
      tenantName: slug,
      role,
      joinedAt: '2026-01-01T00:00:00Z',
    })),
  };
}

interface Harness {
  fixture: ComponentFixture<FirstRunChecklist>;
  tenant: { getOverview: ReturnType<typeof vi.fn> };
  host: HTMLElement;
}

async function createWith(
  options: {
    role?: TenantRole;
    slug?: string;
    slugs?: string[];
    getOverview?: (slug: string) => Observable<TenantOverviewServiceModel>;
  } = {},
): Promise<Harness> {
  TestBed.resetTestingModule();

  const tenant = {
    getOverview: vi.fn(options.getOverview ?? (() => of(overview()))),
  };

  await TestBed.configureTestingModule({
    imports: [FirstRunChecklist],
    providers: [
      provideRouter([]),
      { provide: TenantService, useValue: tenant },
      {
        provide: CurrentUserService,
        useValue: {
          user: () =>
            signedInAs(options.role ?? 'Owner', options.slugs ?? [options.slug ?? 'emberfall']),
        },
      },
    ],
  }).compileComponents();

  const fixture = TestBed.createComponent(FirstRunChecklist);
  fixture.componentRef.setInput('tenantSlug', options.slug ?? 'emberfall');
  fixture.detectChanges();

  return { fixture, tenant, host: fixture.nativeElement as HTMLElement };
}

describe('FirstRunChecklist', () => {
  beforeEach(() => localStorage.clear());

  it('offers the four setup steps to a brand-new community', async () => {
    const { host } = await createWith();

    expect(host.textContent).toContain('Getting started');
    expect(host.querySelectorAll('.fr-step')).toHaveLength(4);
    expect(host.querySelectorAll('.fr-step.is-done')).toHaveLength(0);

    // A checklist, not a wizard: every step is a live link from the first render, none is gated
    // behind finishing the one before it.
    const links = Array.from(host.querySelectorAll<HTMLAnchorElement>('.fr-go'));
    expect(links).toHaveLength(4);
    expect(links.every((link) => link.getAttribute('href')?.startsWith('/t/emberfall/'))).toBe(
      true,
    );
  });

  it('derives each step from what the community has, and un-completes it when that goes away', async () => {
    // The RESTRICTION this whole feature turns on. A persisted "step 3 of 4" would still claim the
    // ranks step was done after the last rank was deleted; a derived one cannot.
    const withRanks = await createWith({ getOverview: () => of(overview({ rankCount: 2 })) });
    expect(withRanks.host.querySelectorAll('.fr-step.is-done')).toHaveLength(1);
    expect(withRanks.host.textContent).toContain('Done');

    const withoutRanks = await createWith({ getOverview: () => of(overview({ rankCount: 0 })) });
    expect(withoutRanks.host.querySelectorAll('.fr-step.is-done')).toHaveLength(0);
  });

  it('counts the invite step done only when somebody else has joined', async () => {
    // memberCount includes the caller, so 1 is "just you" — not "you have members".
    const alone = await createWith({ getOverview: () => of(overview({ memberCount: 1 })) });
    expect(alone.host.querySelectorAll('.fr-step.is-done')).toHaveLength(0);

    const joined = await createWith({ getOverview: () => of(overview({ memberCount: 2 })) });
    expect(joined.host.querySelectorAll('.fr-step.is-done')).toHaveLength(1);
  });

  it('says plainly when the deployment has no Blizzard credentials, and keeps the step reachable', async () => {
    const { host } = await createWith({
      getOverview: () => of(overview({ blizzardConfigured: false })),
    });

    const caveat = host.querySelector('.fr-caveat');
    expect(caveat?.textContent).toContain('no Blizzard credentials');
    // Not a dead end: the manual path is named, and the step is still a live link rather than a
    // disabled control.
    expect(caveat?.textContent).toContain('by hand');
    expect(host.querySelectorAll('.fr-go')).toHaveLength(4);
  });

  it('shows nothing to a plain member, and does not even ask', async () => {
    // The endpoint is TenantOfficer, so asking would earn a 403. A member gets their screens' own
    // empty states instead of somebody else's setup list.
    const { host, tenant } = await createWith({ role: 'Member' });

    expect(tenant.getOverview).not.toHaveBeenCalled();
    expect(host.querySelector('.first-run')).toBeNull();
  });

  it('disappears once every step is done', async () => {
    const { host } = await createWith({
      getOverview: () =>
        of(overview({ hasLinkedGuild: true, rankCount: 1, rosterCount: 12, memberCount: 4 })),
    });

    expect(host.querySelector('.first-run')).toBeNull();
  });

  it('stays out of the way when the overview cannot be read', async () => {
    // No error banner: this is an aside above a roster that is rendering fine, and the roster owns
    // its own error state.
    const { host } = await createWith({ getOverview: () => throwError(() => new Error('down')) });

    expect(host.querySelector('.first-run')).toBeNull();
  });

  it('hides for good when dismissed, and asks nothing on the next visit', async () => {
    const { fixture, host } = await createWith();

    host.querySelector<HTMLButtonElement>('.fr-head button')!.click();
    fixture.detectChanges();
    expect(host.querySelector('.first-run')).toBeNull();

    const revisit = await createWith();
    expect(revisit.host.querySelector('.first-run')).toBeNull();
    expect(revisit.tenant.getOverview).not.toHaveBeenCalled();
  });

  it('survives localStorage being unavailable', async () => {
    // A private window, or site data blocked: the accessor throws rather than returning null, and a
    // checklist must never be the reason a roster fails to render.
    const setItem = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('denied');
    });
    const getItem = vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('denied');
    });

    try {
      const { fixture, host } = await createWith();
      expect(host.querySelector('.first-run')).not.toBeNull();

      host.querySelector<HTMLButtonElement>('.fr-head button')!.click();
      fixture.detectChanges();

      // Still hides for this visit; it simply comes back next time.
      expect(host.querySelector('.first-run')).toBeNull();
    } finally {
      setItem.mockRestore();
      getItem.mockRestore();
    }
  });

  // ---- 8.5's two-tenant assertion ----

  it("takes its state from the ACTIVE community, never from the user's other one", async () => {
    // The user is an owner of both. A is set up; B is brand new. Switching community must re-ask and
    // re-render — carrying A's answer into B would tell an officer their empty community was ready,
    // and carrying B's into A would put a setup list over a working roster.
    const answers: Record<string, TenantOverviewServiceModel> = {
      emberfall: overview({ hasLinkedGuild: true, rankCount: 3, rosterCount: 40, memberCount: 9 }),
      'ashes-of-dawn': overview(),
    };

    const { fixture, tenant, host } = await createWith({
      slug: 'emberfall',
      slugs: ['emberfall', 'ashes-of-dawn'],
      getOverview: (slug: string) => of(answers[slug]),
    });

    // A is fully set up, so the panel is not shown at all.
    expect(tenant.getOverview).toHaveBeenLastCalledWith('emberfall');
    expect(host.querySelector('.first-run')).toBeNull();

    fixture.componentRef.setInput('tenantSlug', 'ashes-of-dawn');
    fixture.detectChanges();

    // B was asked about by name, and B's own state is what rendered.
    expect(tenant.getOverview).toHaveBeenLastCalledWith('ashes-of-dawn');
    expect(host.querySelector('.first-run')).not.toBeNull();
    expect(host.querySelectorAll('.fr-step.is-done')).toHaveLength(0);
    expect(
      Array.from(host.querySelectorAll<HTMLAnchorElement>('.fr-go')).every((link) =>
        link.getAttribute('href')?.startsWith('/t/ashes-of-dawn/'),
      ),
    ).toBe(true);
  });

  it('dismisses per community, never across them', async () => {
    // The dismissal key is tenant-scoped for the same reason the read is: one community's preference
    // is not another's.
    const first = await createWith({ slug: 'emberfall', slugs: ['emberfall', 'ashes-of-dawn'] });
    first.host.querySelector<HTMLButtonElement>('.fr-head button')!.click();
    first.fixture.detectChanges();
    expect(first.host.querySelector('.first-run')).toBeNull();

    const second = await createWith({
      slug: 'ashes-of-dawn',
      slugs: ['emberfall', 'ashes-of-dawn'],
    });
    expect(second.host.querySelector('.first-run')).not.toBeNull();
  });
});
