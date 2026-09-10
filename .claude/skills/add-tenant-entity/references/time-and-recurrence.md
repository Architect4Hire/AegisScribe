# Time, recurrence and signups — the calendar's sharp edges

> Reference for the `add-tenant-entity` skill. Read before building anything with a date on it.

Guilds are international. A raid "at 8pm" means four different instants to the people reading it, and
a calendar that gets this wrong doesn't throw an exception — it just quietly assembles the wrong
people at the wrong hour.

## Three clocks, and you must name which one you mean

| Clock | Where it lives | Used for |
|---|---|---|
| **UTC** | every `DateTimeOffset` column, without exception | storage, comparison, ordering, scheduling |
| **Tenant timezone** | `Tenant.TimeZoneId` (an IANA id like `Europe/Berlin`) | the default rendering, and the clock recurrence is expanded in |
| **Viewer local** | the browser | a secondary, on-hover reading |

**Store UTC. Compute in the tenant's zone. Render both, and always label which is which.** An
unlabelled time on a raid calendar is a bug report waiting to happen.

Use IANA ids (`Europe/Berlin`), not Windows ids or raw offsets. An offset is not a timezone: `+01:00`
is Berlin in January and wrong in July.

## Recurrence: expand server-side, in the tenant's zone, and materialise

Two ways to model a repeating event, and only one of them survives contact with reality:

- ❌ **Store the rule, compute occurrences on read.** Falls apart the moment someone cancels one week,
  moves another, or signs up for a specific date — there's nothing to attach that to.
- ✅ **Store the rule, and materialise concrete occurrences as rows.** Each occurrence is a real
  `CalendarEvent` with its own id, signups and attendance. The rule is a `RecurrenceRule` that
  generated them.

Materialise a **bounded window** — the next N weeks — and extend it on a schedule from the sync
worker. Never materialise "forever"; an unbounded generator plus a weekly job is how you get 40,000
rows for one guild.

Rules that follow from that:

- **Expansion happens in the tenant's timezone**, so "every Tuesday at 20:00" stays 20:00 local across
  a daylight-saving boundary. Expanding in UTC silently shifts the raid by an hour twice a year, which
  is exactly the bug people never suspect.
- **Editing one occurrence detaches it** from the rule. Editing the rule offers "this event" or "this
  and all future" — and *never* silently rewrites occurrences people have already signed up for.
- **Deleting the rule** does not delete past occurrences; attendance history is a record, not a
  projection.
- **The model never expands recurrence.** Natural-language scheduling produces a *rule*, previewed as
  concrete dates for confirmation; server code does the expansion. See `.claude/rules/ai.md`.

## Non-existent and ambiguous local times

Twice a year, a local time either doesn't exist or happens twice. A naive conversion throws or picks
silently.

- **Non-existent** (clocks sprang forward over 02:30): shift forward by the gap. 02:30 becomes 03:30.
- **Ambiguous** (clocks fell back, 02:30 happens twice): take the **first** occurrence, and say so in
  a comment where you decide it.

Decide these once, in one helper, with tests naming a real transition date. Don't let each call site
improvise.

## The signup state machine

Signups are not a boolean. Model the states explicitly:

```
Unknown → Accepted | Tentative | Declined | Benched | Late
                          ↓
                   (event occurs)
                          ↓
        Attended | NoShow | Excused   ← AttendanceRecord, separate entity
```

- **Signup ≠ attendance.** A signup is an intention recorded before; attendance is a fact recorded
  after. They are separate rows because they disagree constantly, and the disagreement is the
  interesting data.
- **Signing up is per-character, not per-user.** A player brings a specific character; the roster
  needs to know which. The signup references a `RosterEntry`.
- **Officers can change anyone's signup; members can change only their own.** Rank check is a policy;
  "is this mine" is a Business rule.
- **Every officer change to someone else's signup is audited.** People notice when they get benched.
- **Signup windows**: opens-at and locks-at, both stored UTC. After lock, only officers may change
  anything.

## Attendance derivation

Attendance can be entered by an officer or derived from WarcraftLogs report participation. Both are
legitimate; be explicit about which:

- Record the **source** on `AttendanceRecord` (`Manual` | `LogDerived`).
- **A derived record never silently overwrites a manual one.** An officer's correction is the truth;
  the log is evidence.
- Log-derived attendance only proves someone was in a report — it can't distinguish "benched but
  present" from "didn't come". Say so in the UI rather than implying certainty.

## Testing

The tests that actually catch things here:

- An event recurring weekly across a **DST transition** keeps its local time.
- A **non-existent** local time and an **ambiguous** one both resolve to the documented choice.
- Editing one occurrence leaves siblings untouched; editing the rule offers both scopes and doesn't
  disturb existing signups.
- A tenant in `America/Chicago` and one in `Europe/Berlin` render the same UTC instant differently —
  and this is a **two-tenant** test, so it doubles as an isolation test.
- Signup after lock is rejected for a member and permitted for an officer, with an audit row.
