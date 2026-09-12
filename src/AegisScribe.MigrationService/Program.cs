using AegisScribe.Domain.Context;
using AegisScribe.Domain.Data;
using AegisScribe.MigrationService;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
// Defensive consistency with the API's registration (see its comment) — this process is one-shot
// today so pooling wouldn't actually be exercised here, but that's not an invariant worth relying on.
builder.Services.AddDbContext<AegisScribeDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("aegisscribedb"));
    options.UseOpenIddict();
});
builder.EnrichSqlServerDbContext<AegisScribeDbContext>();

// AegisScribeDbContext now takes ITenantContext (2.4) — register it directly rather than via
// AddAegisScribeDomain(), which also wires the Auth/Me/Tenant business+facade stacks that need
// UserManager<ApplicationUser> and ICurrentUser (an HTTP-request concept); neither exists in this
// process, and this is never resolved to a real tenant here anyway (migrations and OpenIddict client
// seeding never touch an ITenantScoped entity).
builder.Services.AddScoped<TenantContext>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());

// A separate process/DI container from the API, so it needs its own registration to resolve
// IOpenIddictApplicationManager for client seeding. Core only — no server/validation here.
builder.Services.AddOpenIddict()
    .AddCore(options => options.UseEntityFrameworkCore().UseDbContext<AegisScribeDbContext>());

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
