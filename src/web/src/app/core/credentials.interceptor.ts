import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { SKIP_SIGN_IN_REDIRECT } from './auth-redirect.context';
import { AuthService } from './auth.service';
import { CurrentUserService } from './current-user.service';
import { TENANT_PICKER_PATH } from './tenant.guard';

// Matches the API's tenant-scoped route shape, /api/v{n}/t/{slug}/..., regardless of host.
const TENANT_ROUTE_PATTERN = /\/api\/v\d+\/t\/[^/]+(\/|$)/;

// withCredentials is mandatory and load-bearing: every gateway call is cross-origin, so without it
// the browser sends no session cookie and every request is anonymous. X-Requested-With on
// state-changing requests forces a CORS preflight that only our configured SPA origin passes — the
// second CSRF lock behind SameSite=Lax. See .claude/rules/auth.md -> "Angular side".
//
// 401/404 are handled once, here, for every request: a 401 means the session ended (clear the
// cached identity and send the browser to sign-in); a 404 on a tenant-scoped route means "not
// yours" -- route to the tenant picker rather than letting a component render an error, and never
// distinguish "doesn't exist" from "not a member".
//
// The one exception is a request carrying SKIP_SIGN_IN_REDIRECT: the cached identity is still
// dropped, but the browser stays where it is. That request is asking WHETHER anyone is signed in
// rather than acting as someone who is -- see auth-redirect.context.ts.
export const credentialsInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);
  const currentUser = inject(CurrentUserService);
  const router = inject(Router);

  let outgoing = req.clone({ withCredentials: true });

  if (req.method !== 'GET' && req.method !== 'HEAD') {
    outgoing = outgoing.clone({ setHeaders: { 'X-Requested-With': 'XMLHttpRequest' } });
  }

  return next(outgoing).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse) {
        if (error.status === 401) {
          currentUser.clear();
          if (!req.context.get(SKIP_SIGN_IN_REDIRECT)) {
            authService.login(window.location.pathname + window.location.search);
          }
        } else if (error.status === 404 && TENANT_ROUTE_PATTERN.test(req.url)) {
          void router.navigateByUrl(TENANT_PICKER_PATH);
        }
      }

      return throwError(() => error);
    }),
  );
};
