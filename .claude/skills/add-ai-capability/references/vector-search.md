# Vector search — SQL Server 2025 + EF Core 10

> Reference for the `add-ai-capability` skill. **Parts of this are preview.** Confirm against
> https://learn.microsoft.com/en-us/ef/core/providers/sql-server/vector-search and the SQL Server
> vector docs before writing against a remembered signature.

## What's GA and what isn't

| Feature | Status |
|---|---|
| `VECTOR(n)` column type (1–1,998 dimensions, float32) | **GA** — SQL Server 2025, Azure SQL |
| `VECTOR_DISTANCE(metric, a, b)` — exact search, metrics `cosine`, `euclidean`, `dot` | **GA** |
| EF Core mapping of `SqlVector<float>` + `EF.Functions.VectorDistance` | **GA in EF Core 10** |
| `CREATE VECTOR INDEX` (DiskANN) — approximate search | **Preview**, needs `PREVIEW_FEATURES = ON` |
| EF Core `.VectorSearch(...).WithApproximate()` | **Experimental**, tracks the preview above |
| `VECTOR(n, float16)` half precision | **Preview** |

Two consequences for this repo: the AppHost pins a **2025 SQL Server image** (the 2022 default has no
`VECTOR` type at all), and the database resource turns on `PREVIEW_FEATURES` via a creation script.
Both are in `.claude/rules/aspire.md`.

**No separate NuGet package is needed.** Vector support is built into EF Core 10 and supersedes the
older `EFCore.SqlServer.VectorSearch` preview extension — if you find that package referenced
anywhere, remove it.

## Modelling

```csharp
public class Item
{
    public int Id { get; set; }
    public int BlizzardItemId { get; set; }
    public string Name { get; set; } = null!;
    public string SearchText { get; set; } = null!;      // what gets embedded

    [Column(TypeName = "vector(1536)")]
    public SqlVector<float>? Embedding { get; set; }

    public string? EmbeddingModel { get; set; }          // which model produced it
    public DateTimeOffset? EmbeddedAt { get; set; }
}
```

The dimension in `vector(1536)` must match the embedding model's output exactly. `EmbeddingModel` is
not bookkeeping — it's what lets a backfill find rows produced by a superseded model. A column mixing
vectors from two models returns confidently wrong neighbours with no error anywhere.

Make the column **nullable**. Rows exist before they're embedded, and the backfill is what fills them.

## Index

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Item>().HasVectorIndex(i => i.Embedding, "cosine");
}
```

Which generates:

```sql
CREATE VECTOR INDEX [IX_Items_Embedding] ON [Items] ([Embedding]) WITH (METRIC = COSINE)
```

Constraints on the index worth knowing before you're debugging it: the table needs a **clustered
primary key**, it needs at least **100 rows with non-null vectors**, it can't be partitioned, and the
database needs `PREVIEW_FEATURES = ON`. A vector index migration that fails on an empty table is
working as designed — seed first, or add the index in a later migration.

Pick the metric once and use the same one everywhere. Cosine is the right default for text
embeddings; mixing metrics between the index and the query silently degrades results rather than
erroring.

## Querying

**Exact** (GA, correct up to roughly tens of thousands of rows — which covers the item catalogue
comfortably):

```csharp
var queryVector = new SqlVector<float>(
    await embeddingGenerator.GenerateVectorAsync(userQuery, cancellationToken: ct));

var matches = await context.Items
    .Where(i => i.Embedding != null)
    .OrderBy(i => EF.Functions.VectorDistance("cosine", i.Embedding!.Value, queryVector))
    .Take(20)
    .Select(i => new ItemSummaryServiceModel(i.BlizzardItemId, i.Name, i.Quality, i.ItemLevel))
    .ToListAsync(ct);
```

**Approximate** (preview, for when the catalogue outgrows exact search):

```csharp
var matches = await context.Items
    .VectorSearch(i => i.Embedding, queryVector, "cosine")
    .OrderBy(r => r.Distance)
    .Take(20)
    .WithApproximate()
    .ToListAsync(ct);
```

`WithApproximate()` goes **after** `Take()` and requires the vector index. Start with exact search;
move to approximate when a measurement says to, not before.

Note the query still runs in the **repository**, projecting to a ServiceModel like every other list
read. Vector search is a query technique, not a reason to bypass the layering.

## Generating embeddings

In the sync worker, never per request:

```csharp
var due = await repository.FindUnembeddedItemsAsync(batchSize, _modelId, ct);

foreach (var chunk in due.Chunk(16))
{
    var vectors = await _embeddings.GenerateAsync(chunk.Select(i => i.SearchText).ToList(), ct);
    for (var i = 0; i < chunk.Length; i++)
    {
        chunk[i].Embedding      = new SqlVector<float>(vectors[i].Vector);
        chunk[i].EmbeddingModel = _modelId;
        chunk[i].EmbeddedAt     = DateTimeOffset.UtcNow;
    }
    await repository.SaveAsync(ct);
}
```

Batch the calls — one request per item is slow and expensive for no benefit. Keep batches modest
(16–64) so a failure re-does little work, and make the job resumable from the store: it finds rows
where `Embedding IS NULL OR EmbeddingModel <> @current`, so an interrupted run just continues, and a
model change becomes a re-backfill by construction.

**Compose `SearchText` deliberately.** It's what the embedding actually sees, and it should read like
the thing a user would type: name, item class and subclass, slot, quality, notable stats. Not a JSON
blob, and not the raw Blizzard payload.

## Testing

Integration tests run against real SQL Server (the vector type doesn't exist in the in-memory
provider or SQLite) with **checked-in fixed vectors**. Never call the embedding model from a test —
the suite must be deterministic and must run with no credentials.

A good fixture is three or four short vectors you constructed by hand so the expected ordering is
obvious from reading them. That test then genuinely proves the query and the metric are right, which
a test over opaque real embeddings does not.
