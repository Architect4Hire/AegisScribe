import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthService } from '../../../core/auth.service';
import { Landing } from './landing';

describe('Landing', () => {
  let loginCalls: string[];

  beforeEach(async () => {
    loginCalls = [];

    await TestBed.configureTestingModule({
      imports: [Landing],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: AuthService,
          useValue: {
            login: (returnUrl: string) => loginCalls.push(returnUrl),
            register: () => {
              throw new Error('the hero itself never registers');
            },
          },
        },
      ],
    }).compileComponents();
  });

  it('renders without making a single request — no /me, no tenant', () => {
    const fixture = TestBed.createComponent(Landing);
    fixture.detectChanges();

    TestBed.inject(HttpTestingController).verify();
    expect((fixture.nativeElement as HTMLElement).querySelector('.hero-heading')).not.toBeNull();
  });

  it('carries the wordmark and both calls to action', () => {
    const fixture = TestBed.createComponent(Landing);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('.brand')?.textContent).toContain('AEGISSCRIBE');
    expect(host.querySelector('.hero-cta')?.textContent).toContain('Register');
    expect(host.querySelector('scribe-register-form')).not.toBeNull();
  });

  it('hands "Log in" to a full-page navigation, not a router link or an XHR', () => {
    const fixture = TestBed.createComponent(Landing);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    const loginButton = [...host.querySelectorAll('button')].find((button) =>
      button.textContent?.includes('Log in'),
    );

    // A <button> rather than an <a routerLink>: the OAuth code+PKCE chain is a browser redirect,
    // and an in-app navigation cannot follow it anywhere useful.
    expect(loginButton).toBeDefined();
    expect(loginButton?.getAttribute('routerLink')).toBeNull();

    loginButton?.click();
    expect(loginCalls).toEqual(['/']);
  });
});
