var builder = DistributedApplication.CreateBuilder(args);

var sqlPassword = builder.AddParameter("sql-password", secret: true);
var sql = builder.AddSqlServer("sql", sqlPassword)
    .WithImageTag("2025-latest")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume();
var db = sql.AddDatabase("aegisscribedb");

var migrations = builder.AddProject<Projects.AegisScribe_MigrationService>("migrations")
    .WithReference(db)
    .WaitFor(db);

var redisPassword = builder.AddParameter("redis-password", secret: true);
var cache = builder.AddRedis("cache", password: redisPassword)
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume();

// Blizzard credentials are optional on purpose: with neither configured the gateway degrades to a no-op
// and the app runs on seeded data (external.md). Hence the callback with an empty fallback rather than a
// bare AddParameter — an unresolved parameter makes Aspire prompt, which would block an offline
// `aspire run` on something allowed to be missing. Set them with user secrets, never a literal here.
var blizzardId = builder.AddParameter(
    "blizzard-client-id",
    () => builder.Configuration["Parameters:blizzard-client-id"] ?? string.Empty);
var blizzardSecret = builder.AddParameter(
    "blizzard-client-secret",
    () => builder.Configuration["Parameters:blizzard-client-secret"] ?? string.Empty,
    secret: true);

var api = builder.AddProject<Projects.AegisScribe_ApiService>("api")
    .WithReference(db)
    .WithReference(cache)
    .WithEnvironment("Blizzard__ClientId", blizzardId)
    .WithEnvironment("Blizzard__ClientSecret", blizzardSecret)
    .WaitForCompletion(migrations)
    .WithExternalHttpEndpoints();

// Gets the same credentials because the background refresh that keeps stored rows inside Blizzard's
// 30-day window is the worker's job. It writes the realm catalogue and will take on the stale-row
// refresh in 6.5, so it needs the database — and the schema before it starts, hence WaitForCompletion.
var sync = builder.AddProject<Projects.AegisScribe_SyncWorker>("sync")
    .WithReference(db)
    .WithEnvironment("Blizzard__ClientId", blizzardId)
    .WithEnvironment("Blizzard__ClientSecret", blizzardSecret)
    .WaitForCompletion(migrations);

var gateway = builder.AddProject<Projects.AegisScribe_Gateway>("gateway")
    .WithReference(api)
    .WithReference(cache)
    .WaitFor(api)
    .WithExternalHttpEndpoints();

var web = builder.AddProject<Projects.AegisScribe_Web>("web")
    .WithReference(gateway)
    .WithExternalHttpEndpoints();

var bffSecret = builder.AddParameter("bff-client-secret", secret: true);
var opsSecret = builder.AddParameter("ops-client-secret", secret: true);

migrations.WithReference(gateway);
migrations.WithEnvironment("Oidc__BffClientSecret", bffSecret);
migrations.WithEnvironment("Oidc__OpsClientSecret", opsSecret);

// The gateway needs its own copy to exchange the code and to revoke on logout.
gateway.WithEnvironment("Oidc__BffClientSecret", bffSecret);

// So the gateway's CORS policy can read services:web:https:0 without a literal (1B.8; gateway.md
// -> "CORS"). Declared as its own statement, not chained onto gateway's own declaration above,
// because `web` needs `gateway` declared first — this closes the cycle the other way round.
gateway.WithReference(web);

builder.Build().Run();
