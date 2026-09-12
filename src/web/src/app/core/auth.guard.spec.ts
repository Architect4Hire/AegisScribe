import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';
import { UserServiceModel } from '../models/auth.models';
import { authGuard } from './auth.guard';
import { CurrentUserService } from './current-user.service';

const dummyRoute = {} as unknown as ActivatedRouteSnapshot;
const dummyState = {} as unknown as RouterStateSnapshot;

function configureWith(ensureLoaded: () => Promise<UserServiceModel>): void {
  TestBed.configureTestingModule({
    providers: [{ provide: CurrentUserService, useValue: { ensureLoaded } }],
  });
}

describe('authGuard', () => {
  it('activates once the session is confirmed', async () => {
    configureWith(() => Promise.resolve({} as UserServiceModel));

    const result = await TestBed.runInInjectionContext(() => authGuard(dummyRoute, dummyState));

    expect(result).toBe(true);
  });

  it('blocks activation when the session check fails, without redirecting itself', async () => {
    configureWith(() => Promise.reject(new Error('401')));

    const result = await TestBed.runInInjectionContext(() => authGuard(dummyRoute, dummyState));

    expect(result).toBe(false);
  });
});
