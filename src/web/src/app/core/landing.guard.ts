import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { CurrentUserService } from './current-user.service';
import { TENANT_PICKER_PATH } from './tenant.guard';

// Decides what '/' is. Signed out it is the landing page; signed in it is the tenant picker, because
// a member has no use for a hero telling them what the product does.
//
// This is also the landing pad for the gateway's post-sign-in redirect: /auth/login sends the
// browser back to spaOrigin + returnUrl, which defaults to '/'. So a fresh page load at '/' WITH a
// session is the normal path out of sign-in, not an edge case -- without the probe a user who just
// signed in would be shown the marketing page.
//
// probeSession() never throws and never redirects: the landing page must render for a visitor who
// has no session and for one whose network is down alike. It is the one page in the app that
// depends on no API call succeeding -- no /me, no resolved tenant.
export const landingGuard: CanActivateFn = async () => {
  const currentUser = inject(CurrentUserService);
  const router = inject(Router);

  const user = await currentUser.probeSession();

  return user === null || router.parseUrl(TENANT_PICKER_PATH);
};
