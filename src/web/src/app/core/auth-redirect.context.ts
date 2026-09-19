import { HttpContext, HttpContextToken } from '@angular/common/http';

// Opts a single request out of the credentials interceptor's "401 means sign in" redirect.
//
// Wrong for exactly one caller — the landing guard, which asks GET /me purely to find out whether
// anyone is signed in. There a 401 is the ANSWER, not a failure, and redirecting on it would bounce
// every anonymous visitor into the OAuth flow before they saw the front page.
//
// The redirect only. Not a way to hold a token, skip credentials, or reach api.* directly.
export const SKIP_SIGN_IN_REDIRECT = new HttpContextToken(() => false);

export function skipSignInRedirect(): HttpContext {
  return new HttpContext().set(SKIP_SIGN_IN_REDIRECT, true);
}

// Opts a single request out of the interceptor's "a 404 on a tenant route means not yours" redirect.
//
// For the handful of tenant-scoped writes where a 404 is an ANSWER about the thing being acted on — no
// guild by that name in the game, a guild this community no longer follows — rather than about the
// community itself. The tenant guard has already confirmed membership before any of these screens
// render, so bouncing an officer to the picker because they mistyped a guild name would throw them out
// of a community they are standing in.
//
// Opt in per request, never by default: every read keeps the backstop, which is what catches a
// membership revoked mid-session (auth.md).
export const SKIP_TENANT_MISS_REDIRECT = new HttpContextToken(() => false);

export function skipTenantMissRedirect(): HttpContext {
  return new HttpContext().set(SKIP_TENANT_MISS_REDIRECT, true);
}
