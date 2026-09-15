import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { HttpTestingController } from '@angular/common/http/testing';
import { By } from '@angular/platform-browser';
import { routes } from './app.routes';
import { CurrentUserService } from './core/current-user.service';
import { UserServiceModel } from './models/auth.models';
import { TenantShell } from './features/tenancy/tenant-shell/tenant-shell';
import { CharacterProfile } from './features/character/character-profile/character-profile';
import { RosterTable } from './features/roster/roster-table/roster-table';
import { Landing } from './features/account/landing/landing';
import { AuthService } from './core/auth.service';
import { Router } from '@angular/router';
import { EMPTY } from 'rxjs';

const user: UserServiceModel = {
  id: 'u1',
  email: 'a@b.com',
  displayName: null,
  createdAt: '2026-01-01T00:00:00Z',
  memberships: [
    {
      tenantId: 't1',
      tenantSlug: 'ashes-of-dawn',
      tenantName: 'Ashes of Dawn',
      role: 'Officer',
      joinedAt: '2026-01-01T00:00:00Z',
    },
  ],
};

// This is the one piece of router wiring a plain setInput() render test can't catch: that
// app.config.ts's withComponentInputBinding() actually delivers the live :tenantSlug segment into
// TenantShell.tenantSlug() on a real navigation, not just when constructed directly.
describe('app.routes -> withComponentInputBinding', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter(routes, withComponentInputBinding()),
        {
          provide: CurrentUserService,
          useValue: {
            user: () => user,
            ensureLoaded: () => Promise.resolve(user),
            hasMembership: (tenantSlug: string) =>
              user.memberships.some((membership) => membership.tenantSlug === tenantSlug),
          },
        },
      ],
    });
  });

  it('binds the :tenantSlug route param onto TenantShell.tenantSlug()', async () => {
    const harness = await RouterTestingHarness.create();
    const shell = await harness.navigateByUrl('/t/ashes-of-dawn', TenantShell);

    expect(shell.tenantSlug()).toBe('ashes-of-dawn');
  });

  it('binds the PARENT :tenantSlug onto a child route component', async () => {
    // A different question from the two around it, and the one a setInput() test structurally cannot
    // ask: roster-table and rank-manager sit under /t/:tenantSlug and declare tenantSlug as a
    // required input, but that segment belongs to the PARENT route. Angular only inherits parent
    // params into a child with its own component when paramsInheritanceStrategy says so — so this
    // asserts the wiring, not the component.
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/t/ashes-of-dawn/roster');

    const roster = harness.fixture.debugElement.query(By.directive(RosterTable))
      .componentInstance as RosterTable;

    expect(roster.tenantSlug()).toBe('ashes-of-dawn');

    // Settle the two in-flight reads the component fired, each with the shape its own caller expects
    // — a roster page envelope and a bare rank list. Flushing both with the same empty array leaves
    // the page handler dereferencing an absent `items`, which surfaces as an unhandled error after
    // the test has already passed.
    const http = TestBed.inject(HttpTestingController);
    http
      .match((request) => request.url.endsWith('/roster'))
      .forEach((request) => request.flush({ items: [], nextCursor: null, hasMore: false }));
    http.match((request) => request.url.endsWith('/ranks')).forEach((request) => request.flush([]));
  });

  it('binds the PARENT :tenantSlug onto CharacterProfile on the in-tenant route', async () => {
    // The same component serves two routes, and the failure mode if the parent segment did NOT reach
    // it is silent rather than loud: tenantSlug is optional, so the in-tenant character page would
    // quietly behave as the public front door — no claim lookup, no pill, no button, no error. That
    // is exactly the shape 7.5b works to produce on the OTHER route, which is why it needs its own
    // navigation test rather than a setInput one.
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/t/ashes-of-dawn/characters/eu/argent-dawn/thornwake');

    const profile = harness.fixture.debugElement.query(By.directive(CharacterProfile))
      .componentInstance as CharacterProfile;

    expect(profile.tenantSlug()).toBe('ashes-of-dawn');

    TestBed.inject(HttpTestingController)
      .match((request) => request.url.includes('/characters/'))
      .forEach((request) => request.flush(null, { status: 404, statusText: 'Not Found' }));
  });

  it('renders the character profile on the PUBLIC route with no tenant', async () => {
    // 7.5b's restriction, as a route test rather than a component one: the same component serves the
    // tenant-less front door, and `tenantSlug` is simply undefined there. That is what makes the
    // banner show no claim pill and no claim button — there is no community to claim a character FOR.
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/characters/eu/argent-dawn/thornwake');

    const profile = harness.fixture.debugElement.query(By.directive(CharacterProfile))
      .componentInstance as CharacterProfile;

    expect(profile.tenantSlug()).toBeUndefined();
    expect(profile.region()).toBe('eu');

    // Exactly ONE request — the character. No claim lookup fires without a community, which is the
    // behaviour that keeps this route usable by a visitor with no session at all.
    const http = TestBed.inject(HttpTestingController);
    const requests = http.match(() => true);
    expect(requests.length).toBe(1);
    expect(requests[0].request.url).toContain('/characters/');
    requests[0].flush(null, { status: 404, statusText: 'Not Found' });
  });

  it('binds region/realmSlug/name onto CharacterProfile through the nested route', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/t/ashes-of-dawn/characters/eu/argent-dawn/thornwake');

    const profile = harness.fixture.debugElement.query(By.directive(CharacterProfile))
      .componentInstance as CharacterProfile;

    expect(profile.region()).toBe('eu');
    expect(profile.realmSlug()).toBe('argent-dawn');
    expect(profile.name()).toBe('thornwake');

    // Let the in-flight character fetch resolve so nothing is left pending after the test.
    TestBed.inject(HttpTestingController)
      .expectOne(() => true)
      .flush(null, { status: 404, statusText: 'Not Found' });
  });
});

// The routing decision 5.6b turns on: '/' is two different destinations depending on whether the
// visitor has a session, and the probe behind it must not bounce an anonymous visitor into
// sign-in.
describe('app.routes -> the root route', () => {
  function configure(probeSession: () => Promise<UserServiceModel | null>) {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter(routes, withComponentInputBinding()),
        {
          provide: CurrentUserService,
          useValue: {
            user: () => null,
            probeSession,
            ensureLoaded: () => Promise.resolve(user),
            hasMembership: () => true,
          },
        },
        { provide: AuthService, useValue: { login: () => undefined, register: () => EMPTY } },
      ],
    });
  }

  it('renders the landing page at / for a signed-out visitor', async () => {
    configure(() => Promise.resolve(null));

    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/', Landing);

    expect(harness.routeNativeElement?.querySelector('.hero-heading')).not.toBeNull();
  });

  it('redirects / to the tenant picker for a signed-in visitor', async () => {
    configure(() => Promise.resolve(user));

    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/');

    expect(TestBed.inject(Router).url).toBe('/tenants');
  });
});
