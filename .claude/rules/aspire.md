---
paths:
  - src/AegisScribe.AppHost/**
  - src/AegisScribe.ServiceDefaults/**
---
# Aspire rules — AppHost & ServiceDefaults

The AppHost is the single source of truth for the application model. Keep it declarative.

- **Declare every resource here.** SQL Server, the cache, the Foundry model deployments, the API,
  the sync worker, the migration service, and the Angular app are all added in the AppHost. Nothing
  outside the AppHost invents infrastructure.
- **Wire with the model, not with strings.** Connect services with `WithReference(...)` and order
  startup with `WaitFor(...)` / `WaitForCompletion(...)`. Never hardcode connection strings or
  `localhost:port`; Aspire injects endpoints and connection info via environment/service discovery.
- **Cross-cutting config lives in ServiceDefaults.** OpenTelemetry, health checks, resilience, and
  service discovery are configured once there; every service calls `AddServiceDefaults()`.
- **No business logic in the AppHost.** It orchestrates; it doesn't compute.
- **Local-first.** Backing resources run as local containers for development. An *emulator-backed*
  or locally-run cloud resource is in bounds because it runs locally — `RunAsFoundryLocal()` is to
  Foundry what a container is to SQL Server. What stays out is a resource that needs a real
  subscription: anything reached with `AsExisting`, or provisioned for real.

## SQL Server — pin the 2025 image

The default SQL Server image Aspire uses is a 2022 tag, which has **no `VECTOR` type**. Every AI
feature in this app depends on one. Pin it, and treat an unpinned tag as a bug:

```csharp
var sqlPassword = builder.AddParameter("sql-password", secret: true);

var sql = builder.AddSqlServer("sql", sqlPassword)
                 .WithImageTag("2025-latest")            // ← required for VECTOR
                 .WithLifetime(ContainerLifetime.Persistent)
                 .WithDataVolume();

var db = sql.AddDatabase("aegisscribedb");
```

`WithDataVolume()` plus a persistent lifetime keeps the database across `aspire run` cycles, which
matters here more than usual: re-syncing the item catalog from Blizzard is slow and spends rate
limit, and re-generating embeddings costs model calls.

Approximate vector indexes are still a **preview** SQL Server feature and need `PREVIEW_FEATURES`
turned on for the database. Do that with a creation script on the database resource, not by hand:

```csharp
var db = sql.AddDatabase("aegisscribedb")
            .WithCreationScript("""
                ALTER DATABASE SCOPED CONFIGURATION SET PREVIEW_FEATURES = ON;
                """);
```

## Startup order

Migrations run once, in their own service, before anything that reads the schema:

```csharp
var migrations = builder.AddProject<Projects.AegisScribe_MigrationService>("migrations")
                        .WithReference(db).WaitFor(db);

var api = builder.AddProject<Projects.AegisScribe_ApiService>("api")
                 .WithReference(db).WithReference(cache).WithReference(chat).WithReference(embeddings)
                 .WaitForCompletion(migrations);
```

`WaitForCompletion` (not `WaitFor`) is the point — the API must not start until migrations have
*finished*, not merely started. Aspire also ships `AddEFMigrations(...).RunDatabaseUpdateOnStart()`
as a newer alternative; the separate migration service is what this repo uses, because the vector
index needs raw SQL in a reviewed migration and a dedicated project makes that visible.

## External credentials are parameters

```csharp
var blizzardClientId     = builder.AddParameter("blizzard-client-id");
var blizzardClientSecret = builder.AddParameter("blizzard-client-secret", secret: true);

var sync = builder.AddProject<Projects.AegisScribe_SyncWorker>("sync")
                  .WithReference(db).WithReference(embeddings)
                  .WithEnvironment("Blizzard__ClientId", blizzardClientId)
                  .WithEnvironment("Blizzard__ClientSecret", blizzardClientSecret)
                  .WaitForCompletion(migrations);
```

Set them locally with user secrets (`dotnet user-secrets set Parameters:blizzard-client-secret ...`
in the AppHost project), never in `appsettings.json`, never as a literal in `AppHost.cs`.

## The one pinned port

`.mcp.json` names `http://localhost:8765/sse` for the `appdb` MCP server. MCP client config is read
before the AppHost runs, so it cannot use service discovery — which is why that resource's port is
*pinned* here rather than assigned:

```csharp
.WithEndpoint("http", e => e.Port = 8765)
```

The pin and the literal in `.mcp.json` are two halves of one decision. Change one, change the other.

## Three resources face the internet

```csharp
var api     = builder.AddProject<Projects.AegisScribe_ApiService>("api")
                     .WithReference(db).WithReference(cache)
                     .WithExternalHttpEndpoints();                     // ← public: api.aegisscribe.com

var gateway = builder.AddProject<Projects.AegisScribe_Gateway>("gateway")
                     .WithReference(api).WithReference(cache)
                     .WaitFor(api)
                     .WithExternalHttpEndpoints();                     // ← public: bff.aegisscribe.com

var web     = builder.AddProject<Projects.AegisScribe_Web>("web")
                     .WithReference(gateway)
                     .WithExternalHttpEndpoints();                     // ← public: app.aegisscribe.com
```

**The sync worker and migration service never get one.** They have no HTTP surface anyone should
reach. The API's public hostname is not a convenience any more — the **mobile app calls it directly**,
so it is a genuinely internet-facing service. That comes with the obligations in
`.claude/rules/backend.md` → "The API's public edge": no CORS policy, per-client rate limiting,
mandatory versioning, and OpenAPI in non-production only.

## The mobile app is not an Aspire resource

`AegisScribe.Mobile` is a **client**, not an orchestrated service. It does not appear in the AppHost,
`aspire run` does not launch it, and it gets no `WithReference`. It is built and deployed by the
platform toolchains and points at a running API through its own build configuration. Adding it to the
application model would be modelling a phone as a container.

## OpenIddict's keys are parameters too

```csharp
var oidcSigningCert    = builder.AddParameter("oidc-signing-certificate", secret: true);
var oidcEncryptionCert = builder.AddParameter("oidc-encryption-certificate", secret: true);
var bffClientSecret    = builder.AddParameter("bff-client-secret", secret: true);
```

`AddDevelopmentSigningCertificate()` / `AddDevelopmentEncryptionCertificate()` are for local
development only, and are selected by environment — not by commenting out the real thing. A
certificate committed to the repo is a Blocker.

The gateway resolves the API through **service discovery**
(`AddServiceDiscoveryDestinationResolver()`), so its YARP cluster address is the logical name
`http://api`, never a literal.

When adding a new resource, use the `add-aspire-resource` skill. Verify exact API names
(`AddJavaScriptApp`, `AddFoundry`, client-integration methods, package names) against
https://aspire.dev — these move between versions, and the Foundry integration is preview.
