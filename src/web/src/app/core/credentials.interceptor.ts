import { HttpInterceptorFn } from '@angular/common/http';

// withCredentials is mandatory and load-bearing: every gateway call is cross-origin, so without it
// the browser sends no session cookie and every request is anonymous. X-Requested-With on
// state-changing requests forces a CORS preflight that only our configured SPA origin passes — the
// second CSRF lock behind SameSite=Lax. See .claude/rules/auth.md -> "Angular side".
export const credentialsInterceptor: HttpInterceptorFn = (req, next) => {
  let outgoing = req.clone({ withCredentials: true });

  if (req.method !== 'GET' && req.method !== 'HEAD') {
    outgoing = outgoing.clone({ setHeaders: { 'X-Requested-With': 'XMLHttpRequest' } });
  }

  return next(outgoing);
};
