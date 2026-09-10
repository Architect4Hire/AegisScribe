# Natural-language queries — the constrained filter pattern

> Reference for the `add-ai-capability` skill. This is the pattern for "show me guild members over
> item level 600 who haven't logged in this week". It exists because the obvious implementation —
> ask the model for SQL, then sanitize it — is not securable.

## Why not generated SQL

Sanitizing model-generated SQL is a denylist problem: you're trying to enumerate every string that
could do harm, against a generator that produces novel strings by design, in a language with
comments, nested queries, CTEs, and vendor extensions. Every published attempt has been bypassed. And
even a *correct* query is a problem — nothing in "valid SQL" prevents the model from selecting a
column the caller isn't allowed to read.

So the model doesn't emit a query language. It emits a **small, closed, typed vocabulary**, and
server code — which knows about authorization, which the model does not — turns that into LINQ.

## The shape

```csharp
public enum CharacterField { Realm, Name, Level, Class, Specialization, ItemLevel, Faction, GuildName, LastLogin }
public enum ComparisonOperator { Equals, NotEquals, GreaterThan, LessThan, Contains, Before, After }

public sealed record FilterClause(CharacterField Field, ComparisonOperator Operator, string Value);

public sealed record CharacterFilter(
    IReadOnlyList<FilterClause> Clauses,
    CharacterField? SortBy,
    bool Descending,
    int Limit);
```

Enums, not strings. A field the model invents doesn't deserialize, which means the failure happens at
the boundary rather than three layers in.

Ask for it as **structured output** — a JSON schema the model must conform to — rather than parsing
prose. Then validate anyway; structured output is a strong constraint, not a guarantee.

## The translator

```csharp
public IQueryable<Character> Apply(IQueryable<Character> query, CharacterFilter filter, ClaimsPrincipal user)
{
    if (filter.Clauses.Count > MaxClauses) throw new FilterRejectedException("too many clauses");

    foreach (var clause in filter.Clauses)
    {
        if (!IsReadableBy(clause.Field, user))          // authorization, not just validation
            throw new FilterRejectedException($"field not available: {clause.Field}");

        if (!OperatorAppliesTo(clause.Field, clause.Operator))
            throw new FilterRejectedException($"operator {clause.Operator} does not apply to {clause.Field}");

        query = clause.Field switch
        {
            CharacterField.Level      => ApplyNumeric(query, c => c.Level, clause),
            CharacterField.ItemLevel  => ApplyNumeric(query, c => c.ItemLevel, clause),
            CharacterField.Name       => ApplyText(query, c => c.Name, clause),
            CharacterField.LastLogin  => ApplyDate(query, c => c.LastLoginAt, clause),
            // ... one arm per field, every arm explicit
            _ => throw new FilterRejectedException($"unhandled field: {clause.Field}")
        };
    }

    return query.OrderByField(filter.SortBy, filter.Descending)
                .Take(Math.Clamp(filter.Limit, 1, MaxLimit));
}
```

Five properties this has that a sanitizer doesn't:

1. **Every field is an explicit arm in a switch.** Adding a filterable field is a deliberate code
   change with a code review, not an emergent capability.
2. **Authorization is per-field**, checked against the caller — not against what the model asked for.
3. **Operators are type-checked** against the field. `Contains` on a numeric field is rejected rather
   than coerced into something surprising.
4. **The limit is clamped server-side**, so a model asking for a million rows gets `MaxLimit`.
5. **The default arm throws.** A new enum member without a translator arm fails loudly the first time
   it's used, rather than being silently ignored — which would turn a filter into a no-op and quietly
   widen the result set.

`FilterRejectedException` maps to **400 with a plain explanation**, not 500. This is a normal outcome:
users ask for things the schema doesn't cover, and the honest answer is "I can't filter on that yet".

## What the user sees

Show the interpreted filter back to them — "level > 60, guild = Ashes of Dawn, sorted by item level" —
before or alongside the results. It turns a misinterpretation into something the user can spot and
correct in one read, instead of a wrong answer that looks authoritative. This is the highest-value
piece of UI in the whole AI vertical, and it costs almost nothing.

## The write variant: constrained *commands*

Natural-language scheduling uses the same pattern with the stakes raised. A wrong filter shows the
wrong rows; a wrong command creates or cancels events people organise their evenings around.

```csharp
public enum CalendarVerb { CreateSeries, CreateEvent, MoveEvent, CancelEvent, SetSignupWindow }

public sealed record CalendarCommand(
    CalendarVerb Verb,
    string Title,
    RecurrenceSpec? Recurrence,   // enum frequency + interval + weekdays + count/until
    DateOnly? Date,
    TimeOnly? StartLocal,         // in the TENANT's timezone
    int? DurationMinutes,
    IReadOnlyList<DateOnly>? Skip);
```

Everything a filter needs, plus three things a command needs on top:

1. **Preview before write, always.** Expand the command into the concrete events it would create —
   real dates, real local times, in the tenant's zone — and render them. The model produced a *rule*;
   the human approves the *rows*. This is the single control that makes the feature safe, because a
   recurrence misread is obvious in a list of dates and invisible in a sentence.
2. **Explicit confirmation.** A separate request carrying the previewed command's id. The preview
   response is not a write, and no code path turns a chat turn directly into calendar rows.
3. **Idempotent write.** The confirmation carries the preview id; replaying it does nothing. Someone
   will double-click, and a double-booked raid week is a real support burden.

Also: **the model never expands recurrence.** It emits the rule; server code expands it, in the
tenant's timezone, using the shared helper — see
`.claude/skills/add-tenant-entity/references/time-and-recurrence.md`. Asking a language model to
enumerate dates across a daylight-saving boundary is asking for a wrong answer stated confidently.

Destructive verbs (`CancelEvent`) additionally require that the preview names **exactly which existing
events** would be affected, by title and date, and that anything with signups already attached is
called out separately.

## Required tests

These are security tests, and they're the reason this pattern exists. Each one should fail loudly if
someone later "simplifies" the translator:

- **Unknown field** — a filter naming a field that isn't in the enum is rejected at deserialization.
- **Unhandled field** — an enum member with no switch arm throws, and does not silently pass through.
- **Unauthorized field** — an anonymous or `Member`-role caller filtering on an officer-only field is
  rejected.
- **Operator mismatch** — `Contains` on `Level`, `GreaterThan` on `Faction` — rejected.
- **Over-limit** — `Limit: 1_000_000` is clamped to `MaxLimit`, not honoured.
- **Too many clauses** — rejected rather than translated into a pathological query.
- **Injection attempt in a value** — `Value: "'; DROP TABLE Characters--"` is treated as a literal
  string comparison and matches nothing. Assert the generated SQL parameterises it; that's the test
  that proves the whole approach.
- **No raw SQL path exists** — a repo-wide assertion that no file under `Ai/` references
  `FromSqlRaw`, `ExecuteSqlRaw`, `SqlCommand`, or `FromSqlInterpolated`. Cheap, and it catches the
  regression this document is written to prevent.
- **No model-supplied tenant** — no `[KernelFunction]` signature and no constrained object has a
  tenant id field. Assert it structurally, by reflecting over the plugin types.

For the **write** variant, additionally:

- **Preview writes nothing.** Issue a preview, assert the event count in the database is unchanged.
- **Confirmation is required** — a write attempted without a preview id is rejected.
- **Replay is idempotent** — confirming the same preview twice creates one set of events.
- **A stale preview expires** rather than applying against changed data.
- **DST correctness** — a series spanning a transition keeps its local time, asserted against a real
  transition date.
- **Cancellation names its victims** — the preview for a destructive verb lists the exact events, and
  flags those with existing signups.
