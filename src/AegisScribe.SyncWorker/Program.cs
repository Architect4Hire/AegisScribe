using AegisScribe.Domain.Context;
using AegisScribe.Domain.Data;
using AegisScribe.Domain.Integration.Blizzard;
using AegisScribe.SyncWorker;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();

// Matches the API's registration rather than AddAegisScribeDomain(): this process needs the database and
// the Blizzard gateway, and none of the request stack. AddAegisScribeDomain() wires the Auth/Me/Tenant
// facades, which need UserManager<ApplicationUser> and ICurrentUser — both HTTP-request concepts that do
// not exist here. Same reasoning as MigrationService.
builder.Services.AddDbContext<AegisScribeDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("aegisscribedb"));
    options.UseOpenIddict();
});
builder.EnrichSqlServerDbContext<AegisScribeDbContext>();

// AegisScribeDbContext takes ITenantContext. The worker's realm catalogue runs GLOBALLY, outside any
// tenant (external.md), and Realm is global reference data carrying no TenantId and no query filter — so
// an always-unresolved context is correct here, exactly as in MigrationService. A job that legitimately
// iterates tenants sets the ambient tenant for the work it does inside; none does yet.
builder.Services.AddScoped<TenantContext>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());

builder.Services.AddScoped<IRealmRepository, RealmRepository>();
builder.Services.AddScoped<ICharacterRepository, CharacterRepository>();

builder.Services.AddOptions<SyncWorkerOptions>()
    .BindConfiguration(SyncWorkerOptions.SectionName)
    .Validate(
        o => o.CharacterBatchSize > 0,
        "SyncWorker:CharacterBatchSize must be positive — a zero batch means nothing is ever refreshed " +
        "and stored rows age past the Terms of Use thirty-day window.")
    .Validate(
        o => o.CharacterPollInterval > TimeSpan.Zero,
        "SyncWorker:CharacterPollInterval must be positive.")
    .Validate(
        o => o.MaxConcurrentRefreshes > 0,
        "SyncWorker:MaxConcurrentRefreshes must be positive.")
    .Validate(
        o => o.MaxConcurrentRefreshes <= o.CharacterBatchSize,
        "SyncWorker:MaxConcurrentRefreshes must not exceed CharacterBatchSize — more workers than work " +
        "is a bound that bounds nothing.")
    .ValidateOnStart();

// Brings the typed HttpClient, the token provider, the shared rate limiter and the staleness options.
builder.Services.AddBlizzardIntegration();

builder.Services.AddScoped<RealmCatalogueSync>();
builder.Services.AddHostedService<RealmCatalogueSyncWorker>();

// The realm catalogue fills the table every character hangs from; this one keeps the
// characters themselves inside the thirty-day window. Both run globally, outside any tenant.
builder.Services.AddScoped<CharacterRefreshSync>();
builder.Services.AddHostedService<CharacterRefreshWorker>();

// Guild rosters. Global like the rest: a Guild row exists only because somebody linked it, so
// refreshing every stale Guild row covers every community without iterating tenants.
builder.Services.AddScoped<IGuildRepository, GuildRepository>();
builder.Services.AddScoped<IGuildSyncDataLayer, GuildSyncDataLayer>();
builder.Services.AddScoped<GuildRefreshSync>();
builder.Services.AddHostedService<GuildRefreshWorker>();

var host = builder.Build();
host.Run();
