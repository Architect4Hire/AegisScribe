using AegisScribe.ApiService.Data;
using AegisScribe.MigrationService;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.AddSqlServerDbContext<AegisScribeDbContext>("aegisscribedb");
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
