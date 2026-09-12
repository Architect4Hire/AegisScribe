import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { UserServiceModel } from '../models/auth.models';
import { CurrentUserService } from './current-user.service';

const user: UserServiceModel = {
  id: 'u1',
  email: 'a@b.com',
  displayName: 'A',
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

describe('CurrentUserService', () => {
  let service: CurrentUserService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(CurrentUserService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('fetches GET /api/v1/me and caches the result', async () => {
    const promise = service.ensureLoaded();
    httpMock.expectOne('/api/v1/me').flush(user);

    expect(await promise).toEqual(user);
  });

  it('does not re-fetch once cached', async () => {
    const first = service.ensureLoaded();
    httpMock.expectOne('/api/v1/me').flush(user);
    await first;

    expect(await service.ensureLoaded()).toEqual(user);
    httpMock.expectNone('/api/v1/me');
  });

  it('shares one in-flight request between concurrent callers', async () => {
    const [a, b] = [service.ensureLoaded(), service.ensureLoaded()];
    httpMock.expectOne('/api/v1/me').flush(user);

    expect(await a).toEqual(user);
    expect(await b).toEqual(user);
  });

  it('clear() drops the cache so the next call re-fetches', async () => {
    const first = service.ensureLoaded();
    httpMock.expectOne('/api/v1/me').flush(user);
    await first;

    service.clear();

    const second = service.ensureLoaded();
    httpMock.expectOne('/api/v1/me').flush(user);
    await second;
  });

  it('hasMembership reflects the cached memberships once loaded', async () => {
    const load = service.ensureLoaded();
    httpMock.expectOne('/api/v1/me').flush(user);
    await load;

    expect(service.hasMembership('ashes-of-dawn')).toBe(true);
    expect(service.hasMembership('emberwatch')).toBe(false);
  });

  it('hasMembership is false before anything has loaded', () => {
    expect(service.hasMembership('ashes-of-dawn')).toBe(false);
  });
});
