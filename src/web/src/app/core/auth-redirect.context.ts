import { HttpContext, HttpContextToken } from '@angular/common/http';

// Opts a single request out of the credentials interceptor's "401 means sign in" redirect.
//
// The interceptor's default is right for every authenticated call: a 401 there means the session
// ended and the browser should go to sign-in. It is wrong for exactly one caller — the landing
// guard, which asks GET /me purely to find out whether anyone is signed in at all. There a 401 is
// the ANSWER ("nobody is"), not a failure, and redirecting on it would bounce every anonymous
// visitor off the front page and into the OAuth flow before they ever saw it.
//
// This opts out of the redirect only. It is not a way to hold a token, skip credentials, or reach
// api.* directly -- see .claude/rules/auth.md -> "Angular side".
export const SKIP_SIGN_IN_REDIRECT = new HttpContextToken(() => false);

export function skipSignInRedirect(): HttpContext {
  return new HttpContext().set(SKIP_SIGN_IN_REDIRECT, true);
}
