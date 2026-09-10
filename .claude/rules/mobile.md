---
paths:
  - src/AegisScribe.Mobile/**
---
# Mobile rules — the .NET MAUI client

The mobile app is an **OAuth 2.0 public client** talking directly to `api.aegisscribe.com`. It does
not go through the gateway, has no cookie, and holds real tokens. Read `.claude/rules/auth.md` for
the server half and `.claude/rules/api-contract.md` for what the API promises a client it cannot
redeploy.

The single sentence that governs this file: **anything shipped to a device is public.** No secret, no
key, no credential, no internal URL you would mind seeing published. That is not a precaution, it is
a description of what a decompiled app is.

## The auth flow

Authorization code + **PKCE**, in the **system browser**, per RFC 8252.

```csharp
var verifier  = RandomNumberGenerator.GetBytes(32).ToBase64Url();
var challenge = SHA256.HashData(Encoding.ASCII.GetBytes(verifier)).ToBase64Url();

var result = await WebAuthenticator.Default.AuthenticateAsync(new WebAuthenticatorOptions
{
    Url         = new Uri($"{ApiBase}/connect/authorize?client_id=aegisscribe-mobile"
                        + $"&response_type=code&scope=openid%20profile%20offline_access"
                        + $"&code_challenge={challenge}&code_challenge_method=S256"
                        + $"&redirect_uri=aegisscribe%3A%2F%2Fauth%2Fcallback&state={state}"),
    CallbackUrl = new Uri("aegisscribe://auth/callback"),
    PrefersEphemeralWebBrowserSession = false,
});
```

**The trap: `result.AccessToken` is null here, and that is correct.** `WebAuthenticatorResult` exposes
`AccessToken` because it also supports implicit-style callbacks. An authorization code flow returns
`code`, not a token. Read `result.Properties["code"]`, verify `state`, then POST it with the
`code_verifier` to `connect/token`. If you find yourself reaching for a flow that puts a token in the
callback URL to make `AccessToken` populate, you have swapped a secure flow for a deprecated one.

**`WebAuthenticator` does not implement PKCE for you.** It performs the browser round-trip and hands
back the callback parameters. The verifier, the challenge, the `state` check and the token exchange
are all the app's job.

**Never use an embedded `WebView` for sign-in.** RFC 8252 §8.12 — an in-app WebView lets the host app
read the user's credentials as they type them, defeats the platform credential manager and password
autofill, and shares no session with the real browser. `WebAuthenticator` uses **Custom Tabs** on
Android and **ASWebAuthenticationSession** on iOS, both of which are out-of-process by design. An
embedded WebView login screen is a Blocker.

`scope=offline_access` is what makes a refresh token come back. Without it the user is signed out
after fifteen minutes and nobody will be able to work out why.

### Per-platform callback registration

The custom scheme `aegisscribe://` must be registered or the browser has nowhere to return to:

- **Android** — an activity deriving from `WebAuthenticatorCallbackActivity` with an intent filter for
  `DataScheme = "aegisscribe"`, plus the Custom Tabs `<queries>` element in the manifest, which
  Android 11+ requires or the browser silently fails to resolve.
- **iOS** — `CFBundleURLTypes` in `Info.plist` declaring the same scheme.

Both are easy to forget and both fail as "nothing happens after login", not as an error.

## Token storage and lifecycle

**Refresh tokens go in `SecureStorage`** — Keychain on iOS, Keystore-backed `EncryptedSharedPreferences`
on Android. Never `Preferences`, never a file, never a static field that a crash dump would carry.
The access token may live in memory only; it lasts fifteen minutes and re-obtaining it is cheap.

**Refresh is single-flight.** Several requests 401 at once on a reconnect, and the naive handler fires
several refreshes. Guard it with one `SemaphoreSlim`: the first caller refreshes, the rest await the
result and retry with the new token.

The server's 30-second reuse leeway (`.claude/rules/auth.md`) exists to forgive the race you lose
here, not to excuse not handling it. **Do not treat the leeway as the design.**

Refreshing rotates the token, so **persist the new refresh token before using it**. Losing it in a
crash between "server redeemed the old one" and "app wrote the new one" signs the user out.

A refresh that fails with `invalid_grant` is a genuine sign-out: clear storage and route to login.
A refresh that fails on a network error is **not** — retry with backoff and leave the user signed in.
Conflating these logs people out every time they walk into a lift.

**Sign-out calls `connect/revoke` first**, then clears storage. Clearing alone leaves a live refresh
token in the database that a device backup could restore.

## The HTTP layer

One `HttpClient`, one `DelegatingHandler` that attaches the token and handles 401-refresh-retry.
Nothing else in the app builds an `Authorization` header.

- **Retry with jitter** on transient failures and on 429 — and **honour `Retry-After`**. A retry storm
  from a few thousand phones is a self-inflicted outage.
- **Timeouts on every call.** A mobile request that hangs holds a spinner forever; the default
  `HttpClient` timeout of 100 seconds is far past the point a user has decided the app is broken.
- **`X-Client-Version` and `X-Client-Platform` on every request** — this is what makes the API's
  deprecation and forced-upgrade path work at all.
- **Handle `426 Upgrade Required`** with a blocking screen linking to the store. It is the one error
  the app cannot retry its way out of.
- **Handle `304`, send `If-None-Match`.** Background refresh on a metered connection is the user's
  data allowance.
- **Send `Idempotency-Key` on every create.** Generate it once per user intent and reuse it across
  retries — regenerating it per attempt is the same as not sending it.
- Deserialization **ignores unknown fields** and maps unknown enum values to `Unknown`. This is what
  lets the API add fields without breaking a shipped app; it must be true from the first commit.

## Tenancy is unchanged

The tenant slug is in the route, exactly as on the web: `/api/v1/t/{tenantSlug}/...`. **The app never
sends a tenant id**, in a header, a body or a token claim — the same rule as everywhere else in this
system (`.claude/rules/tenancy.md`).

The app displays based on the user's role; it never enforces. A hidden officer button is cosmetics,
and the server policy behind it is the actual control. A 404 on a tenant route means "not yours" —
route to the community picker rather than showing an error.

## Offline

The app caches for reading, not for writing. Rosters, calendars and character detail are cached and
refreshed with `?since=` delta sync (`.claude/rules/api-contract.md`), including tombstones — without
them a cancelled raid stays on the phone forever.

**Writes are not queued offline.** A signup composed on the Underground and submitted an hour later,
against a raid that has since filled, is a worse experience than being told the network is down. If
offline writes are ever wanted, they need conflict resolution designed on purpose, not a retry queue
bolted on.

Cached tenant data is **cleared on sign-out and on leaving a community.** A stale roster surviving a
removal is a cross-tenant leak that happens to live on a phone.

## Push notifications

Push is a **third delivery channel** alongside in-app and Discord, and it follows the same pipeline
(`.claude/skills/add-notification/SKILL.md`).

- Device registrations are **tenant-scoped**: user + tenant + platform token. Unregister on sign-out
  and on leaving a community, or the phone keeps receiving another community's business.
- **The payload carries no private content.** A push notification renders on a lock screen; it says
  "New event in Emberfall", not the officer note. It carries an id the app fetches properly once
  unlocked, with the user's own token and the server's own authorization.
- A failed platform token is retired after repeated rejection, exactly like a dead Discord webhook.

## What is deliberately not here

- **No certificate pinning.** It breaks on every certificate rotation, is bypassed trivially on a
  rooted device, and the threat it addresses is largely handled by platform TLS. Revisit only with a
  specific threat model.
- **No client secret**, in any form. See the top of this file.
- **No AI prompts or model configuration in the app.** The AI vertical lives behind the API
  (`.claude/rules/ai.md`); a prompt shipped in a binary is a published prompt.
- **The mobile app is not an Aspire resource.** It is a client, not an orchestrated service. Its API
  base URL comes from build configuration pointing at a running API, and `aspire run` does not launch
  it.
