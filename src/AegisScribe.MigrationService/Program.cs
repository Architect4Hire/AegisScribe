using AegisScribe.ApiService.Data;
using AegisScribe.MigrationService;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.AddSqlServerDbContext<AegisScribeDbContext>("aegisscribedb",
    configureDbContextOptions: options => options.UseOpenIddict());

// A separate process/DI container from the API, so it needs its own registration to resolve
// IOpenIddictApplicationManager for client seeding. Core only — no server/validation here.
builder.Services.AddOpenIddict()
    .AddCore(options => options.UseEntityFrameworkCore().UseDbContext<AegisScribeDbContext>());

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
