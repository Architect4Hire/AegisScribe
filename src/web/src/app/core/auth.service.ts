import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { RuntimeConfigService } from './runtime-config.service';

// Login and logout are the gateway's own concerns, not the API's (gateway.md -> "The gateway is an
// OAuth confidential client"). Neither call carries a token anywhere near this service — the SPA
// never handles one (.claude/rules/auth.md).
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly runtimeConfig = inject(RuntimeConfigService);

  // A top-level browser navigation, not a fetch: connect/authorize's sign-in page and the OAuth
  // redirect chain need the real location to change, so this deliberately bypasses HttpClient (and
  // the credentials interceptor) entirely.
  login(returnUrl = '/'): void {
    const url = new URL('/auth/login', this.runtimeConfig.gatewayUrl());
    url.searchParams.set('returnUrl', returnUrl);
    window.location.href = url.toString();
  }

  logout(): Observable<void> {
    return this.http.post<void>(`${this.runtimeConfig.gatewayUrl()}/auth/logout`, null);
  }
}
