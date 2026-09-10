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

var api = builder.AddProject<Projects.AegisScribe_ApiService>("api");

var sync = builder.AddProject<Projects.AegisScribe_SyncWorker>("sync");

var web = builder.AddViteApp("web", "../web")
    .WithRunScript("start")
    .WithReference(api);

builder.Build().Run();
