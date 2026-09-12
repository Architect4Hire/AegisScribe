import { inject } from '@angular/core';
import { CanActivateFn } from '@angular/router';
import { CurrentUserService } from './current-user.service';

// Confirms the caller is signed in before the route activates. On failure the interceptor has
// already redirected the browser to sign-in (a 401 from GET /me), so this guard only needs to
// block activation until that's known -- it never duplicates the redirect.
export const authGuard: CanActivateFn = async () => {
  const currentUser = inject(CurrentUserService);

  try {
    await currentUser.ensureLoaded();
    return true;
  } catch {
    return false;
  }
};
