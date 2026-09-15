import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { RegisterViewModel, UserServiceModel } from '../models/auth.models';
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

  // The exception to the two above, and the reason it is an ordinary XHR: creating an Identity
  // account is a plain API write proxied through the gateway, not a leg of the token flow. Nothing
  // about the response signs anyone in — the caller discards it and hands off to login() for the
  // interactive step.
  register(viewModel: RegisterViewModel): Observable<UserServiceModel> {
    return this.http.post<UserServiceModel>(
      `${this.runtimeConfig.gatewayUrl()}/api/v1/auth/register`,
      viewModel,
    );
  }
}
