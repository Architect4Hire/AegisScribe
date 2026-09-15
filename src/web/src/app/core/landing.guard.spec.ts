import { TestBed } from '@angular/core/testing';
import { Router, UrlTree } from '@angular/router';
import { provideRouter } from '@angular/router';
import { landingGuard } from './landing.guard';
import { CurrentUserService } from './current-user.service';
import { TENANT_PICKER_PATH } from './tenant.guard';
import { UserServiceModel } from '../models/auth.models';

const user: UserServiceModel = {
  id: 'u1',
  email: 'a@b.com',
  displayName: null,
  createdAt: '2026-01-01T00:00:00Z',
  memberships: [],
};

function runGuard(probeSession: () => Promise<UserServiceModel | null>) {
  TestBed.configureTestingModule({
    providers: [provideRouter([]), { provide: CurrentUserService, useValue: { probeSession } }],
  });

  return TestBed.runInInjectionContext(
    () =>
      landingGuard(
        null as never,
        null as never,
      ) as Promise<boolean | UrlTree>,
  );
}

describe('landingGuard', () => {
  it('renders the landing page when nobody is signed in', async () => {
    expect(await runGuard(() => Promise.resolve(null))).toBe(true);
  });

  it('redirects a signed-in visitor to the tenant picker', async () => {
    const result = await runGuard(() => Promise.resolve(user));

    expect(result).toEqual(TestBed.inject(Router).parseUrl(TENANT_PICKER_PATH));
  });
});
