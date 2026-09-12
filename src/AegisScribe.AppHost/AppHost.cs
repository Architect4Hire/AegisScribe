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

var api = builder.AddProject<Projects.AegisScribe_ApiService>("api")
    .WithReference(db)
    .WithReference(cache)
    .WaitForCompletion(migrations)
    .WithExternalHttpEndpoints();

var sync = builder.AddProject<Projects.AegisScribe_SyncWorker>("sync");

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

// The gateway needs its own copy to exchange the code and to revoke on logout (1B.6).
gateway.WithEnvironment("Oidc__BffClientSecret", bffSecret);

// So the gateway's CORS policy can read services:web:https:0 without a literal (1B.8; gateway.md
// -> "CORS"). Declared as its own statement, not chained onto gateway's own declaration above,
// because `web` needs `gateway` declared first — this closes the cycle the other way round.
gateway.WithReference(web);

builder.Build().Run();
