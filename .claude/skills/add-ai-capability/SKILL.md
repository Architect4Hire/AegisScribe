---
name: add-ai-capability
description: >
  Add or extend an AI feature in AegisScribe — a Semantic Kernel plugin, a RAG-grounded chat answer, a
  semantic search over items or spells, a natural-language character query, or a generated summary.
  Use for requests like "make the crafting advisor suggest an upgrade", "let users search items in
  plain English", "generate a guild progression summary", "add a kernel function for professions".
  Covers the SK plugin shape, embedding generation and backfill, SQL Server vector search through EF
  Core, the constrained-filter pattern that keeps the model away from SQL, and the tests each needs.
---

# Add an AI capability

Work in `src/AegisScribe.ApiService/Ai/` (and `src/AegisScribe.SyncWorker/Embeddings/` for backfill).
Read `.claude/rules/ai.md` first — it holds the invariants; this skill is the procedure.

Read the reference for the flavour you're building:
- `references/vector-search.md` — embeddings, `SqlVector<float>`, the EF Core 10 query shapes, index
  and migration mechanics.
- `references/nl-query-safety.md` — the constrained-filter pattern, in full, with its test list.

## First, decide which of the five shapes this is

| Shape | What it is | Where the work goes |
|---|---|---|
| **Kernel function** | A capability the model can call — "get this character's equipment" | `Ai/Plugins/`, wrapping a facade |
| **Semantic search** | Embed the query, find nearest items/spells by vector distance | Repository query + an embedded column |
| **NL query (read)** | Plain-English filter over characters or the roster | Constrained *filter* object → LINQ translator |
| **NL command (write)** | Plain-English scheduling — "raid Tues and Thurs 8pm for six weeks" | Constrained *command* object → preview → confirm → write |
| **Generated prose** | A summary, analysis or assessment, cached | Facade-cached, grounded, cited, labelled in the UI |

They compose — the crafting advisor is a chat that calls kernel functions and runs a semantic search —
but each has its own failure mode, so build them one at a time.

**The write shape is new and it is the riskiest thing in the AI vertical**, higher-risk than the query
filter: a mistaken filter shows the wrong rows, a mistaken command creates or destroys real events
people organise their evenings around. It uses the same constrained-object pattern with three
additions — **preview as concrete rows, explicit confirmation, idempotent write** — set out in
`references/nl-query-safety.md`.

## Tenancy applies to every one of them

- A `[KernelFunction]` **never takes a tenant id the model can fill.** The tenant comes from ambient
  context. A function signature with `tenantId` in it is a hole with a docstring.
- **Tenant-scoped vector search filters on `TenantId` inside the SQL**, not by dropping rows
  afterwards — post-filtering leaks through counts and ordering.
- The **item catalogue's vectors are global**; event descriptions, officer notes and applications are
  tenant-scoped. Know which you're querying before you write the query.
- Cached AI output is cached per tenant, under a tenant-prefixed key.

## Output about people owes more

Attendance insight, recruitment fit and loot guidance produce opinions officers may act on. Each one
**cites the rows it used**, **states what it doesn't know**, **describes behaviour rather than
character** ("signed up for 4 of the last 12", never "unreliable"), and **is visible to the person
it's about**. A tool that generates private assessments of members is a different product and not this
one. See `.claude/rules/ai.md`.

## Steps

1. **Register the model clients once.** In `Program.cs`, via the Aspire client integration keyed to
   the AppHost deployment names, resolved as the Microsoft.Extensions.AI abstractions:
   ```csharp
   builder.AddAzureAIInferenceChatClient("chat").AsIChatClient();
   // and the embedding generator, keyed to the "embeddings" deployment
   ```
   Nothing outside this registration names a provider type. Everything downstream injects
   `IChatClient` or `IEmbeddingGenerator<string, Embedding<float>>`.

2. **Kernel functions wrap facades.** A plugin is an adapter, not a place for logic:
   ```csharp
   public sealed class ProfessionPlugin(IProfessionFacade professions)
   {
       [KernelFunction, Description("List the recipes a character can currently craft, with reagents.")]
       public Task<IReadOnlyList<RecipeServiceModel>> GetCraftableAsync(
           string realm, string name, CancellationToken ct)
           => professions.GetCraftableRecipesAsync(realm, name, ct);
   }
   ```
   Inject a **facade**. Not a `DbContext`, not a repository, not `IBlizzardGateway`. This is what
   makes authorization, validation and caching apply to the AI path without being rebuilt there — a
   plugin that queries EF directly has silently opted out of all three.

   Write the `[Description]` for the model, not for a developer: it is the entire basis on which the
   model decides whether to call this function. "Get profession data" gets called at random; "List
   the recipes a character can currently craft, with reagents" gets called when it should be.

3. **Semantic search needs three things**, in this order:
   - a `SqlVector<float>` column on the entity, dimensioned to the embedding model,
   - **generation in the sync worker**, when the entity is created or its text changes — never per
     request,
   - a repository query ordering by `EF.Functions.VectorDistance("cosine", ...)`.

   Mechanics, index syntax and the approximate-search variant are in `references/vector-search.md`.
   The one thing to internalise here: the vector column is **bound to a specific embedding model and
   dimension**, recorded alongside it. Changing the model is a migration plus a full re-backfill, not
   a config edit, and a mixed-model column returns confidently wrong neighbours.

4. **Natural-language queries never produce SQL.** The model emits a **constrained filter object** —
   structured output over enums of whitelisted fields and operators — which server code validates and
   translates to LINQ. `references/nl-query-safety.md` has the pattern and the required tests. The
   short version: there is no code path in this solution where model output reaches `FromSqlRaw`,
   `ExecuteSqlRaw`, or a `SqlCommand`, and an unrecognised field is a rejected request rather than a
   best-effort guess.

5. **Ground every answer, and cache the prose.** Retrieval first, then the prompt. If retrieval
   returns nothing, the feature says it doesn't know — it does not let the model fill the gap from
   training data. Wrong item stats stated confidently are worse than no answer, and this domain has
   a decade of outdated content in every model's training set.

   Generated summaries cache in the **facade**, keyed against the character's `LastSyncedAt`, so an
   unchanged character doesn't get re-summarised on every page view. That's the default failure mode
   of this feature and it costs real money.

6. **Prompts are files.** One per capability under `Ai/Prompts/`, loaded and cached — not string
   literals scattered through classes. They're reviewable and diffable that way, which matters more
   for prompts than for code, because a prompt regression is invisible in a stack trace.

7. **Fence untrusted text.** Character names, guild names and guild notes are player-authored and
   arrive from Blizzard. So do item descriptions. When they enter a prompt, they go inside a clearly
   delimited block labelled as data, and the system prompt says instructions inside it are to be
   ignored. Assume someone has named a character something adversarial, because someone has.

8. **Scope the context.** Never place data in a prompt that the requesting user isn't already
   authorized to read. Going through facades makes this automatic; going around them makes it your
   problem.

9. **Bound every call.** A `CancellationToken` and a timeout on every model call, and token usage
   recorded. ServiceDefaults already wires OpenTelemetry and Microsoft.Extensions.AI emits spans — an
   AI feature whose cost you can't see in the dashboard isn't finished.

10. **Tests.**
    - **Plugin:** unit test with a mocked facade — that the function calls it and shapes the result.
      Also assert the plugin's constructor takes a facade and nothing lower; that's the invariant.
    - **Semantic search:** integration test against real SQL Server with a handful of seeded vectors —
      a known query returns the expected neighbour in the expected order. Use fixed, checked-in
      vectors, not live model calls; the test must be deterministic and must not need credentials.
    - **NL query:** the whitelist tests from `references/nl-query-safety.md`. These are the highest-
      value tests in the AI vertical — an unknown field, an out-of-range limit, an operator that
      doesn't apply to the field's type, and a filter naming a field the caller can't read all get
      **rejected**, and every one of them is a security test, not a validation nicety.
    - **Grounding:** with a stubbed `IChatClient`, assert that the prompt actually contains the
      retrieved context, and that an **empty retrieval** produces the "I don't know" path rather than
      a model call.
    - **Caching:** a second identical summary request doesn't reach the model; a changed
      `LastSyncedAt` does.
    - Never write a test that asserts on model prose. Assert on what was *sent*, what was *called*,
      and what was *cached*.

## Checklist before done
- [ ] Model clients resolved as `IChatClient` / `IEmbeddingGenerator`; no provider type outside DI
- [ ] No `[KernelFunction]` takes a model-supplied tenant id; tenant comes from ambient context
- [ ] Tenant-scoped vector search filters on `TenantId` **in SQL**, not after the fact
- [ ] Any write-shaped capability previews concrete rows, requires explicit confirmation, and is
      idempotent
- [ ] Output about people cites its rows, states its gaps, describes behaviour not character, and is
      visible to the person it describes
- [ ] Every SK plugin injects a **facade** — no `DbContext`, repository, or gateway
- [ ] `[KernelFunction]` descriptions are written for the model and say when to call the function
- [ ] No model output reaches raw SQL anywhere; NL queries go through the constrained filter
- [ ] Unknown field / operator / over-limit filters are **rejected**, with tests
- [ ] Embeddings generated in the worker, not per request; model + dimension recorded with the column
- [ ] Answers are grounded; empty retrieval produces "I don't know", not a guess
- [ ] Untrusted Blizzard-sourced text is fenced and labelled as data in prompts
- [ ] Prompt context is scoped to what the caller may read
- [ ] Generated prose is cached against `LastSyncedAt` in the facade
- [ ] Prompts live in files under `Ai/Prompts/`
- [ ] Every model call has a `CancellationToken` and a timeout; token usage is traced
- [ ] UI labels generated content as generated (see `.claude/rules/frontend.md`)
- [ ] Tests pass and are deterministic — no live model calls in the suite (`dotnet test`)
