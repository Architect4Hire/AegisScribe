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

  it('binds region/realmSlug/name onto CharacterProfile through the nested route', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/t/ashes-of-dawn/characters/eu/argent-dawn/thornwake');

    const profile = harness.fixture.debugElement.query(By.directive(CharacterProfile))
      .componentInstance as CharacterProfile;

    expect(profile.region()).toBe('eu');
    expect(profile.realmSlug()).toBe('argent-dawn');
    expect(profile.name()).toBe('thornwake');

    // Let the in-flight character fetch resolve so nothing is left pending after the test.
    TestBed.inject(HttpTestingController).expectOne(() => true).flush(null, { status: 404, statusText: 'Not Found' });
  });
});
