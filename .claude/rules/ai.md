---
paths:
  - src/AegisScribe.ApiService/Ai/**
  - src/AegisScribe.SyncWorker/Embeddings/**
---
# AI rules — Microsoft.Extensions.AI + Semantic Kernel + Azure AI Foundry

The AI vertical lives in `src/AegisScribe.ApiService/Ai/`. It is a *consumer* of the layered stack, not
a second way into the database.

## The layering, stated once

- **Abstractions:** code against `IChatClient` and `IEmbeddingGenerator<string, Embedding<float>>`
  from Microsoft.Extensions.AI. Nothing outside DI registration names a provider type.
- **Provider:** Azure AI Foundry, registered through the Aspire client integration
  (`builder.AddAzureAIInferenceChatClient("chat").AsIChatClient()`), keyed to the AppHost deployment
  resource name. `RunAsFoundryLocal()` in the AppHost gives the same interface offline.
- **Orchestration:** Semantic Kernel on top — plugins, function calling, prompt templates. SK
  consumes the Microsoft.Extensions.AI abstractions rather than replacing them; don't register a
  parallel SK-native chat service alongside `IChatClient`.
- **Vector store:** SQL Server `VECTOR` columns on our own entities, through EF Core 10. One
  database. Details in `.claude/skills/add-ai-capability/references/vector-search.md`.

## Tenancy is not optional here either

The AI path is the easiest place in the app to leak across tenants, because a model will happily
follow an instruction to look at "the other guild".

- **A plugin never takes a tenant id as a parameter the model can fill.** The tenant comes from the
  ambient `ITenantContext`. A `[KernelFunction]` with a `tenantId` argument is a hole with a docstring.
- **Vector search over tenant-scoped content** — event descriptions, officer notes, recruitment
  applications — filters on `TenantId` **inside the SQL query**, never by dropping rows afterwards.
  Post-filtering leaks through counts and ordering even when the rows themselves never render.
- **The item catalogue's vectors are global** and carry no tenant. Know which kind you're querying.
- Grounding context is scoped to what the caller may read **in the resolved tenant** — going through
  facades makes that automatic, which is the whole reason for the rule below.

## Plugins call facades. Always.

A Semantic Kernel plugin is a thin adapter over an `I<Feature>Facade`:

```csharp
public sealed class CharacterPlugin(ICharacterFacade characters)
{
    [KernelFunction, Description("Get a character's equipped items by realm and name.")]
    public Task<CharacterEquipmentServiceModel> GetEquipmentAsync(string realm, string name, CancellationToken ct)
        => characters.GetEquipmentAsync(realm, name, ct);
}
```

It injects a facade, never a `DbContext`, never a repository, never `IBlizzardGateway`. This is not
tidiness — it's how authorization, validation and caching apply to the AI path without being
reimplemented there. A plugin that queries EF directly has silently opted out of all three.

## The capabilities, and what each one owes

| Capability | Shape | The thing it must not do |
|---|---|---|
| **Crafting & gear advisor** | RAG chat over a character's gear + the recipe catalogue | Answer from training data when retrieval is empty |
| **Semantic item search** | Vector search over the global item catalogue | Mix embedding models in one column |
| **NL character/roster query** | Constrained filter object → LINQ | Emit SQL, or filter on a field the caller can't read |
| **Character & guild summaries** | Generated prose, cached against `LastSyncedAt` | Regenerate on every page view |
| **Roster & composition analysis** | Signups × class/spec capability → gap list | State a gap it can't point at a signup for |
| **Attendance & reliability insight** | Attendance history → per-member summary | Editorialise about people; see below |
| **NL scheduling** | Constrained *command* object → calendar writes | Write to the calendar without confirmation |
| **Recruitment fit & loot guidance** | Applicant/drop scored against roster need | Present a ranking as fact |

Two of these are new shapes worth stating plainly:

**Natural-language scheduling is a write path**, which makes it the highest-risk AI feature in the
app — higher than the query filter, because a mistake creates or destroys real events people organise
their evenings around. It uses the same constrained-object pattern as the query filter (see
`references/nl-query-safety.md`), with three additions: the command object is **previewed as
concrete events** before anything is written, the user **confirms explicitly**, and the write is
**idempotent** so a double-confirm doesn't double-book. Recurrence is expanded server-side, in the
tenant's timezone, and never by the model.

**Judgement-shaped output is about people.** Attendance insight, recruitment fit and loot guidance
produce opinions that officers may act on. Each one:

- **cites the rows it used** — the signups, the attendance records, the roster entries — so a member
  can check the working;
- **states what it doesn't know** ("this covers signups since March; three raids have no recorded
  attendance");
- **describes behaviour, not character** — "signed up for 4 of the last 12" is a fact; "unreliable" is
  a verdict the tool doesn't get to render;
- is **visible to the person it's about**, or it isn't built. A tool that generates private assessments
  of members is a different product and not this one.

## The model never writes SQL

Natural-language character queries are the highest-risk feature here, and the safe pattern is not
"generate SQL and sanitize it". The model emits a **constrained filter object** — structured output
matching a schema of whitelisted fields and operators — which server code validates and translates
into LINQ:

```csharp
public sealed record CharacterFilter(
    IReadOnlyList<FilterClause> Clauses, string? SortBy, bool Descending, int Limit);

public sealed record FilterClause(CharacterField Field, ComparisonOperator Operator, string Value);
```

`CharacterField` and `ComparisonOperator` are **enums**. An unparseable field name is a rejected
request, not a fallback. The translator caps `Limit`, refuses fields the caller isn't authorized to
filter on, and never string-concatenates anything into a query. There is no code path in this
solution where model output reaches `FromSqlRaw`, `ExecuteSqlRaw`, or a `SqlCommand`.

Full pattern and the test list in
`.claude/skills/add-ai-capability/references/nl-query-safety.md`.

## Prompts

- **Ground, don't recall.** Answers about characters, items and recipes come from retrieved context
  in the prompt. If retrieval returns nothing, the feature says it doesn't know — it does not let the
  model fill the gap from training data. Wrong item stats stated confidently are worse than no answer.
- **Scope context to the caller.** Never place data in a prompt that the requesting user isn't
  already authorized to read. Grounding goes through facades precisely so this is automatic.
- **Blizzard-sourced text is untrusted input.** Character names, guild names and guild notes are
  written by players. Fence them in the prompt, label them as data, and never let them carry
  instructions. Treat retrieved item descriptions the same way.
- **System prompts live in files**, not in string literals scattered through classes — one prompt per
  file under `Ai/Prompts/`, loaded and cached. They're reviewable that way, and diffable.
- **Every call is cancellable and has a timeout.** A model call is a network call with a worse tail
  latency than most. It never blocks a request thread without a `CancellationToken`.

## Cost and determinism

- **Embeddings are generated once, in the sync worker**, when an item is created or its text
  changes — never per request. The backfill is a batch job with bounded concurrency, and it records
  which model and dimension produced each vector. Changing the embedding model invalidates the
  column: that's a migration plus a re-backfill, not a config edit.
- **Cache what's stable.** AI-written character and guild summaries are cached against the
  character's `LastSyncedAt` — regenerating an identical summary on every page view is the default
  failure mode of this feature. Cache in the facade, like everything else.
- Keep the chat and embedding deployments as separate AppHost resources with separate names, so they
  can be sized, swapped and traced independently.

## Observability

Model calls are traced like any other dependency — ServiceDefaults already wires OpenTelemetry, and
Microsoft.Extensions.AI emits spans. Record token usage. An AI feature you can't see the cost of in
the dashboard isn't finished.

## Verify before trusting

This is the fastest-moving surface in the stack. The Foundry hosting integration
(`Aspire.Hosting.Foundry`) is **preview** and was recently renamed from `Aspire.Hosting.Azure.AIFoundry`.
SQL Server's approximate vector index is **preview**. Semantic Kernel remains supported, but
Microsoft has positioned the **Microsoft Agent Framework** as the successor for agent orchestration —
this project deliberately uses SK, and that's a decision, not an oversight; don't silently migrate it,
and don't write SK code that a future move to MAF would have to unpick. Confirm package names and API
shapes against https://aspire.dev and https://learn.microsoft.com before writing against a
remembered signature.
