import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { skipSignInRedirect } from './auth-redirect.context';
import { AuthService } from './auth.service';
import { credentialsInterceptor } from './credentials.interceptor';
import { CurrentUserService } from './current-user.service';
import { TENANT_PICKER_PATH } from './tenant.guard';

describe('credentialsInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let loginCalls: string[];
  let clearCalls: number;
  let navigateCalls: string[];

  beforeEach(() => {
    loginCalls = [];
    clearCalls = 0;
    navigateCalls = [];

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([credentialsInterceptor])),
        provideHttpClientTesting(),
        {
          provide: AuthService,
          useValue: { login: (returnUrl: string) => loginCalls.push(returnUrl) },
        },
        { provide: CurrentUserService, useValue: { clear: () => clearCalls++ } },
        {
          provide: Router,
          useValue: {
            navigateByUrl: (url: string) => {
              navigateCalls.push(url);
              return Promise.resolve(true);
            },
          },
        },
      ],
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('sets withCredentials on every request', () => {
    http.get('/api/v1/characters').subscribe();

    const req = httpMock.expectOne('/api/v1/characters');
    expect(req.request.withCredentials).toBe(true);
    req.flush({});
  });

  it('adds X-Requested-With on state-changing requests only', () => {
    http.get('/api/v1/characters').subscribe();
    expect(httpMock.expectOne('/api/v1/characters').request.headers.has('X-Requested-With')).toBe(false);

    http.post('/api/v1/t/ashes-of-dawn/roster', {}).subscribe();
    expect(
      httpMock.expectOne('/api/v1/t/ashes-of-dawn/roster').request.headers.has('X-Requested-With'),
    ).toBe(true);
  });

  it('clears the cached identity and redirects to sign-in on a 401, from any request', async () => {
    const result = firstValueFrom(http.get('/api/v1/me')).catch((error: unknown) => error);
    httpMock.expectOne('/api/v1/me').flush(null, { status: 401, statusText: 'Unauthorized' });
    await result;

    expect(clearCalls).toBe(1);
    expect(loginCalls.length).toBe(1);
  });

  it('clears the identity but does NOT redirect when the request opts out', async () => {
    const result = firstValueFrom(http.get('/api/v1/me', { context: skipSignInRedirect() })).catch(
      (error: unknown) => error,
    );
    httpMock.expectOne('/api/v1/me').flush(null, { status: 401, statusText: 'Unauthorized' });
    await result;

    expect(clearCalls).toBe(1);
    expect(loginCalls).toEqual([]);
  });

  it('routes to the tenant picker on a 404 from a tenant-scoped route', async () => {
    const result = firstValueFrom(http.get('/api/v1/t/ashes-of-dawn/roster')).catch((error: unknown) => error);
    httpMock.expectOne('/api/v1/t/ashes-of-dawn/roster').flush(null, { status: 404, statusText: 'Not Found' });
    await result;

    expect(navigateCalls).toEqual([TENANT_PICKER_PATH]);
  });

  it('does not treat a 404 from a non-tenant route as a tenant miss', async () => {
    const result = firstValueFrom(http.get('/api/v1/characters/argent-dawn/thornwake')).catch(
      (error: unknown) => error,
    );
    httpMock
      .expectOne('/api/v1/characters/argent-dawn/thornwake')
      .flush(null, { status: 404, statusText: 'Not Found' });
    await result;

    expect(navigateCalls).toEqual([]);
  });

  it('rethrows the original error to the caller', async () => {
    const result = firstValueFrom(http.get('/api/v1/me')).catch((error: unknown) => error);
    httpMock.expectOne('/api/v1/me').flush(null, { status: 401, statusText: 'Unauthorized' });

    const error = (await result) as { status: number };
    expect(error.status).toBe(401);
  });
});
