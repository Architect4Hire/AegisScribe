import { HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { CurrentUserService } from './current-user.service';

// Not yet a registered route — the tenant-picker page is 5.6's scope. Until then this path is
// where a rejected tenant navigation lands with no route to activate; see the 5.5 plan.
export const TENANT_PICKER_PATH = '/tenants';

// Confirms the route's :tenantSlug is one the caller belongs to before the route activates. A
// 404 on a tenant route means "not yours" -- this is the client-side head start on that same
// rule, and it must never distinguish "no such tenant" from "not a member": both cases are just
// "not in my cached memberships", and both land on the tenant picker. The interceptor's own 404
// handler is the defense-in-depth backstop for whatever this cached check misses (e.g. a
// membership revoked mid-session). See .claude/rules/auth.md -> "Angular side".
export const tenantGuard: CanActivateFn = async (route) => {
  const currentUser = inject(CurrentUserService);
  const router = inject(Router);

  const tenantSlug = route.paramMap.get('tenantSlug');
  if (!tenantSlug) {
    return router.parseUrl(TENANT_PICKER_PATH);
  }

  try {
    await currentUser.ensureLoaded();
  } catch (error) {
    if (error instanceof HttpErrorResponse && error.status === 401) {
      // The interceptor already redirected to sign-in.
      return false;
    }
    // Any other failure (a genuine outage, not an auth problem) has nowhere better to land than
    // the tenant picker, which owns its own retryable error state -- leaving the router with no
    // match at all would be worse than a designed error screen.
    return router.parseUrl(TENANT_PICKER_PATH);
  }

  return currentUser.hasMembership(tenantSlug) || router.parseUrl(TENANT_PICKER_PATH);
};
