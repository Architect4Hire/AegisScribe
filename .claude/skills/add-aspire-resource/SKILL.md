---
name: add-aspire-resource
description: >
  Add a new locally-orchestrated resource (database, cache, model deployment, message queue,
  container, or another project) to the AegisScribe Aspire AppHost and wire it into the services that
  use it. Use for requests like "add a Redis cache", "add a second model deployment", "add a
  background worker", "run this container alongside the API". Keeps everything local and driven
  through service discovery.
---

# Add an Aspire resource

Work primarily in `src/AegisScribe.AppHost/`. The goal: declare the resource, then reference it — never
hardcode connection details.

1. **Add the hosting package** if needed: `aspire add <resource>` (or add the
   `Aspire.Hosting.<Resource>` package). Confirm the exact package name at https://aspire.dev — some
   of these have been renamed recently (`Aspire.Hosting.Azure.AIFoundry` → `Aspire.Hosting.Foundry`).
2. **Declare the resource in the AppHost.** Keep resources as local containers or local runtimes.
3. **Wire it into consumers.** Add `.WithReference(x)` to the projects that use it, and `.WaitFor(x)`
   so they start after it's healthy. Use `.WaitForCompletion(x)` for a run-once resource like the
   migration service.
4. **Consume it in the service** via the matching Aspire client integration, keyed to the resource
   name — the connection is injected, not configured by hand.
5. **Verify.** Run `aspire run`, open the dashboard, and confirm the resource is healthy and the
   consuming service connected.

## The shapes this repo uses

**SQL Server** — the image tag is not optional, the 2022 default has no `VECTOR` type:
```csharp
var sqlPassword = builder.AddParameter("sql-password", secret: true);
var sql = builder.AddSqlServer("sql", sqlPassword)
                 .WithImageTag("2025-latest")
                 .WithLifetime(ContainerLifetime.Persistent)
                 .WithDataVolume();
var db = sql.AddDatabase("aegisscribedb");
```
Consume with `builder.AddSqlServerDbContext<AegisScribeDbContext>("aegisscribedb")`
(`Aspire.Microsoft.EntityFrameworkCore.SqlServer`). If the context is already registered by hand,
use `EnrichSqlServerDbContext<T>` instead of registering it twice.

**Redis**:
```csharp
var cache = builder.AddRedis("cache");
```
Consume with `builder.AddRedisDistributedCache("cache")`.

**Azure AI Foundry** (preview integration — verify the API before writing against it):
```csharp
var foundry = builder.AddFoundry("foundry").RunAsFoundryLocal();   // local for dev
var chat       = foundry.AddDeployment("chat", FoundryModel.OpenAI.Gpt5Mini);
var embeddings = foundry.AddDeployment("embeddings", /* embedding model */);
```
Consume with `builder.AddAzureAIInferenceChatClient("chat").AsIChatClient()`
(`Aspire.Azure.AI.Inference`). Chat and embedding deployments stay **separate named resources** so
they can be sized, swapped and traced independently.

**Another project**:
```csharp
var sync = builder.AddProject<Projects.AegisScribe_SyncWorker>("sync")
                  .WithReference(db).WithReference(embeddings)
                  .WaitForCompletion(migrations);
```

**A secret the resource needs** is a parameter, never a literal:
```csharp
var secret = builder.AddParameter("blizzard-client-secret", secret: true);
```
Set it with user secrets on the AppHost project. If you are about to type a key into `AppHost.cs` or
`appsettings.json`, stop — the `secret-guard` hook will block the write anyway.

## Rules
- Everything stays local — no cloud resources needing a real subscription in this build. A locally
  run cloud runtime (`RunAsFoundryLocal()`, an emulator container) is in bounds; `AsExisting` is not.
- No connection strings, keys, or ports in application code; the AppHost + service discovery own that.
  The one pinned port (`8765`, for the `appdb` MCP server) is documented in CLAUDE.md → Restrictions.
- The AppHost stays declarative — no business logic, no HTTP calls, no data shaping.

## Checklist before done
- [ ] Resource declared in the AppHost as a local container/runtime
- [ ] Consumers wired with `WithReference` + `WaitFor` (or `WaitForCompletion` for run-once)
- [ ] Service consumes it via the Aspire client integration (name-keyed, injected)
- [ ] Any credential is an `AddParameter(..., secret: true)`, not a literal
- [ ] SQL Server, if touched, still pins a 2025 image tag
- [ ] Dashboard shows the resource healthy and connected
