import { HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { CurrentUserService } from './current-user.service';

export const TENANT_PICKER_PATH = '/tenants';

// Confirms the route's :tenantSlug is one the caller belongs to before the route activates. It must
// never distinguish "no such tenant" from "not a member" — both are just "not in my cached
// memberships", and both land on the tenant picker. The interceptor's 404 handler is the backstop for
// whatever this cached check misses, such as a membership revoked mid-session (auth.md).
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
    // A genuine outage has nowhere better to land than the tenant picker, which owns its own
    // retryable error state — no route match at all would be worse than a designed error screen.
    return router.parseUrl(TENANT_PICKER_PATH);
  }

  return currentUser.hasMembership(tenantSlug) || router.parseUrl(TENANT_PICKER_PATH);
};
