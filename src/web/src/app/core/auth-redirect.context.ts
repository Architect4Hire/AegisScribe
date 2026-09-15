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
