---
name: add-notification
description: >
  Add a new notification to AegisScribe — something that happens in the app producing an in-app alert
  and, where the tenant has configured one, a Discord message. Use for requests like "notify the guild
  when an event is created", "remind people who haven't signed up", "tell officers when someone
  applies", "post a weekly digest to Discord", "send a push notification", "add a notification
  preference". Covers the event → notification → delivery pipeline, per-tenant Discord webhooks,
  mobile push, preferences, retry, and the tests.
---

# Add a notification

Three channels, one pipeline. The **in-app notification centre is the system of record** — every
notification exists there whether or not it also went to Discord or a phone. Discord and push are
delivery channels on top, and either can fail without losing the notification.

Email is deliberately out of scope (CLAUDE.md → Scope). Don't add it because it seems easy.

## The pipeline

```
Domain event          Notification rows           Delivery
(signup locked)  →    one per recipient      →    in-app  (immediate, it IS the row)
                      respecting prefs            Discord (async, worker, retried)
                                                  push    (async, worker, retried)
```

**Adding push did not add a second pipeline.** It is one more channel behind the same fan-out and the
same preferences. If you find yourself writing a parallel path for push, stop — a notification the
user has muted must be muted everywhere, and two pipelines is how that stops being true.

Three properties that make this work and are easy to lose:

- **Raising is synchronous and cheap; delivering is asynchronous.** The facade that creates an event
  raises a domain notification and returns. It does **not** call Discord inline — a Discord outage
  must never fail a signup.
- **Fan-out happens once, at raise time**, against the recipient list and their preferences. Don't
  recompute "who should see this" at delivery time; the roster may have changed by then and the
  notification would go somewhere it wasn't meant to.
- **Everything is tenant-scoped.** `Notification`, `NotificationPreference`, `DiscordWebhook` and
  `DeviceRegistration` all carry `TenantId` and a query filter. See `add-tenant-entity`.

## Push, specifically

A device registration is **user + tenant + platform token**, and being tenant-scoped is the point: a
phone signed into two communities gets that community's notifications, and leaving a community stops
them. Unregister on sign-out and on removal from a tenant, or the phone keeps receiving a roster's
business after the person has left it — a cross-tenant leak that happens to live on someone's device.

**The payload carries no private content.** It renders on a lock screen, in front of whoever is
holding the phone. Send "New event in Emberfall" plus an id; the app fetches the detail once
unlocked, with the user's own token, through the same authorized endpoint as everything else. An
officer note or an application's contents in a push body is a Blocker.

A platform token that the provider rejects repeatedly is retired, exactly like a dead Discord
webhook. Phones are reinstalled, restored and handed on constantly, so a dead-token path is normal
operation rather than an error case.

Push preferences are **per kind, per tenant**, like the other channels — "everything on Discord,
only mentions on my phone" is the common configuration, and a single global toggle cannot express it.

## Steps

1. **Define the notification type** as an enum member plus a template, not a free string:
   ```csharp
   public enum NotificationKind
   {
       EventCreated, EventUpdated, EventCancelled, SignupReminder,
       RosterRankChanged, ApplicationReceived, WeeklyDigest, SyncFailed
   }
   ```
   Templates live in files under `Notifications/Templates/`, one per kind, with named placeholders —
   same reasoning as prompts and GraphQL queries living in files.

2. **Raise it from the facade** that owns the action, after the write commits. A notification for a
   write that got rolled back is worse than no notification, so raise **after** the transaction, not
   inside it.

3. **Resolve recipients and honour preferences.** `NotificationPreference` is per user, per tenant,
   per kind, per channel — a person can want event reminders in Discord but rank changes only in-app,
   and can want different things in two communities.
   - Default new preferences to **on for in-app, on for Discord** for event-shaped notifications, and
     **officer-only** for administrative ones (`ApplicationReceived`, `SyncFailed`).
   - A digest is one notification to a channel, not one per member. Don't fan out a digest.

4. **Write the `Notification` rows.** That is the in-app centre — there is no separate mechanism.
   Unread state is a column, not a second table.

5. **Queue Discord delivery** as a `NotificationDispatch` row for the worker to pick up. Not a
   fire-and-forget `Task.Run`; the process can die between the write and the send, and a queue row
   survives that.

6. **Deliver from the sync worker**:
   - POST the webhook URL with an embed. Keep the payload small and readable — Discord truncates.
   - **Respect Discord's rate-limit headers**, per webhook, and back off. A guild with a busy calendar
     will hit them.
   - **Retry with exponential backoff**, bounded. After the bound, mark the dispatch failed and raise
     a `SyncFailed` notification to the tenant's officers — a silently dead webhook is the most common
     "the app is broken" report.
   - A `404`/`401` from Discord means the webhook was deleted or revoked: mark it invalid, stop
     retrying, and tell the officers. Don't keep hammering a dead URL.
   - Record every attempt: what was sent, to which tenant's webhook, and the outcome.

7. **The webhook URL is a credential.** Stored on `DiscordWebhook`, **encrypted at rest**, never
   logged, never returned to the client in full — the settings screen shows the channel name and a
   masked URL, and lets you replace it. Anyone holding that URL can post to that channel.

8. **Tests.**
   - Raise → the right recipients get rows, and someone who opted out gets none.
   - Raise happens **after** commit: a rolled-back write produces no notification.
   - A Discord failure **does not** fail the originating request, and leaves a retryable dispatch.
   - Retry exhaustion marks the dispatch failed and notifies officers.
   - A 401/404 marks the webhook invalid and stops retrying.
   - The webhook URL never appears in a log or an API response.
   - **Two-tenant:** tenant A's notification never reaches tenant B's webhook or notification centre.
     This one is the whole ballgame — a mis-scoped webhook lookup posts one guild's business into
     another guild's Discord.

## Checklist before done
- [ ] Notification kind is an enum member with a template file, not an inline string
- [ ] Raised from the facade **after** the transaction commits
- [ ] Recipients and preferences resolved at raise time, not delivery time
- [ ] `Notification` rows are the in-app centre; no parallel mechanism
- [ ] Discord delivery is queued and handled by the worker — never inline in a request
- [ ] Rate-limit headers respected per webhook; retries bounded with backoff
- [ ] Dead webhook (401/404) marked invalid, retries stopped, officers told
- [ ] Webhook URL encrypted at rest, masked in responses, absent from logs
- [ ] All four entities tenant-scoped with query filters and tenant-prefixed cache keys
- [ ] **Two-tenant test**: no cross-tenant delivery, in any channel
- [ ] `dotnet test` green
