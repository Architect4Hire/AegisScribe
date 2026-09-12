import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, RouterStateSnapshot, convertToParamMap, provideRouter } from '@angular/router';
import { UserServiceModel } from '../models/auth.models';
import { CurrentUserService } from './current-user.service';
import { TENANT_PICKER_PATH, tenantGuard } from './tenant.guard';

const dummyState = {} as unknown as RouterStateSnapshot;

const memberOfAshesOfDawn: UserServiceModel = {
  id: 'u1',
  email: 'a@b.com',
  displayName: null,
  createdAt: '2026-01-01T00:00:00Z',
  memberships: [
    {
      tenantId: 't1',
      tenantSlug: 'ashes-of-dawn',
      tenantName: 'Ashes of Dawn',
      role: 'Member',
      joinedAt: '2026-01-01T00:00:00Z',
    },
  ],
};

function routeWithSlug(tenantSlug: string | null): ActivatedRouteSnapshot {
  return {
    paramMap: convertToParamMap(tenantSlug ? { tenantSlug } : {}),
  } as unknown as ActivatedRouteSnapshot;
}

function configureWith(load: () => Promise<UserServiceModel>): void {
  let loaded: UserServiceModel | null = null;

  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      {
        provide: CurrentUserService,
        useValue: {
          ensureLoaded: async () => {
            loaded = await load();
            return loaded;
          },
          hasMembership: (tenantSlug: string) =>
            loaded?.memberships.some((membership) => membership.tenantSlug === tenantSlug) ?? false,
        },
      },
    ],
  });
}

describe('tenantGuard', () => {
  it('sends the caller to the tenant picker when the route carries no tenant slug', async () => {
    configureWith(() => Promise.resolve(memberOfAshesOfDawn));

    const result = await TestBed.runInInjectionContext(() => tenantGuard(routeWithSlug(null), dummyState));

    expect(result.toString()).toBe(TENANT_PICKER_PATH);
  });

  it('blocks activation on a 401, without redirecting itself', async () => {
    configureWith(() => Promise.reject(new HttpErrorResponse({ status: 401 })));

    const result = await TestBed.runInInjectionContext(() =>
      tenantGuard(routeWithSlug('ashes-of-dawn'), dummyState),
    );

    expect(result).toBe(false);
  });

  it('sends a genuine failure (not a 401) to the tenant picker rather than leaving no route active', async () => {
    configureWith(() => Promise.reject(new HttpErrorResponse({ status: 500 })));

    const result = await TestBed.runInInjectionContext(() =>
      tenantGuard(routeWithSlug('ashes-of-dawn'), dummyState),
    );

    expect(result.toString()).toBe(TENANT_PICKER_PATH);
  });

  it('activates for a tenant the caller is a member of', async () => {
    configureWith(() => Promise.resolve(memberOfAshesOfDawn));

    const result = await TestBed.runInInjectionContext(() =>
      tenantGuard(routeWithSlug('ashes-of-dawn'), dummyState),
    );

    expect(result).toBe(true);
  });

  it('sends an unknown tenant and a real tenant the caller does not belong to the identical destination', async () => {
    configureWith(() => Promise.resolve(memberOfAshesOfDawn));

    const unknownTenant = await TestBed.runInInjectionContext(() =>
      tenantGuard(routeWithSlug('no-such-tenant'), dummyState),
    );
    const notAMember = await TestBed.runInInjectionContext(() =>
      tenantGuard(routeWithSlug('emberwatch'), dummyState),
    );

    expect(unknownTenant.toString()).toBe(TENANT_PICKER_PATH);
    expect(notAMember.toString()).toBe(TENANT_PICKER_PATH);
    expect(unknownTenant.toString()).toBe(notAMember.toString());
  });
});
