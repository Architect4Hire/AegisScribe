using System.Globalization;
using AegisScribe.ApiService.Infrastructure;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AegisScribe.Tests.Auth;

/// <summary>
/// Boots the real AppHost once for the collection, so integration tests exercise the actual
/// distributed app rather than a fake. The SQL/Redis containers are ContainerLifetime.Persistent, so
/// this attaches to whatever is already running locally.
/// </summary>
public class AegisScribeAppFixture : IAsyncLifetime
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    /// The anonymous-IP permit limit this fixture's API runs with. Production is 20 a minute,
    /// deliberately tight (backend.md → "The API's public edge") and far below what a suite of
    /// integration tests needs. The limiter is still wired, still partitioned by IP, still returns 429
    /// with Retry-After — only the number differs, and <see cref="TightRateLimitAppFixture"/> keeps
    /// the production number for the tests whose subject IS the limit.
    /// </summary>
    protected virtual int AnonymousPermitLimit => 10_000;

    /// <summary>
    /// The per-tenant sync budget this fixture's API runs with, in Blizzard calls per window.
    /// Production is 2,000, which would take hundreds of requests to exhaust over HTTP; six means two
    /// character refreshes and then a 429. Same mechanism, smaller number.
    /// </summary>
    public const int SyncBudgetCallsPerWindow = 6;

    /// <summary>Calls one character refresh costs: the summary, the equipment and the renders.</summary>
    public const int CallsPerCharacterRefresh = 3;

    public DistributedApplication App { get; private set; } = null!;
    public HttpClient ApiClient { get; private set; } = null!;
    public string OpsClientSecret { get; private set; } = null!;
    public string BffSecret { get; private set; } = null!;
    public Uri GatewayBaseAddress { get; private set; } = null!;
    public Uri SpaOrigin { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.AegisScribe_AppHost>();

        appHost.Services.ConfigureHttpClientDefaults(clientBuilder =>
        {
            clientBuilder.AddStandardResilienceHandler();
        });

        appHost.Services.AddLogging(logging => logging
            .AddFilter("Default", LogLevel.Warning)
            .AddFilter("Aspire.Hosting.Dcp", LogLevel.Warning));

        // The migration service seeds aegisscribe-ops with this same plaintext value, from the
        // identical parameter store, so this is what mints a real token.
        OpsClientSecret = appHost.Configuration["Parameters:ops-client-secret"]
            ?? throw new InvalidOperationException("ops-client-secret parameter not configured.");

        // Likewise for aegisscribe-bff. Edge verification drives a direct code+PKCE exchange as this
        // client to reach a real refresh token, since the gateway's own session never exposes one.
        const string bffParamPrefix = "Parameters:bff-client-";
        const string bffParamSuffix = "secret";
        BffSecret
            = appHost.Configuration[bffParamPrefix + bffParamSuffix]
            ?? throw new InvalidOperationException("bff client parameter not configured.");

        var api = appHost.Resources.OfType<ProjectResource>().Single(resource => resource.Name == "api");
        appHost.CreateResourceBuilder(api)
            // The AppHost declares the Blizzard credentials as required parameters so a developer is
            // prompted for them. Overridden empty here, replacing the parameter reference, so the suite
            // neither waits on a prompt nobody will answer nor calls real Blizzard with a developer's
            // saved credentials -- the gateways no-op, which is what these tests run against.
            .WithEnvironment("Blizzard__ClientId", string.Empty)
            .WithEnvironment("Blizzard__ClientSecret", string.Empty)
            .WithEnvironment(
                "RateLimits__AnonymousPermitLimit",
                AnonymousPermitLimit.ToString(CultureInfo.InvariantCulture))
            .WithEnvironment(
                "TenantSyncBudget__CallsPerWindow",
                SyncBudgetCallsPerWindow.ToString(CultureInfo.InvariantCulture));

        // The sync worker is not under test from here and is not free: it connects to SQL and runs its
        // Blizzard passes on startup. CharacterRefreshSyncTests and RealmCatalogueSyncTests exercise
        // those jobs directly, so dropping it costs a process per boot and loses no coverage.
        //
        // `web` stays: gateway.WithReference(web) supplies the CORS origin this fixture resolves
        // SpaOrigin from. It never has to become healthy, only exist.
        var sync = appHost.Resources.SingleOrDefault(resource => resource.Name == "sync");
        if (sync is not null)
        {
            appHost.Resources.Remove(sync);
        }

        App = await appHost.BuildAsync().WaitAsync(DefaultTimeout);
        await App.StartAsync().WaitAsync(DefaultTimeout);
        await App.ResourceNotifications.WaitForResourceHealthyAsync("api").WaitAsync(DefaultTimeout);
        await App.ResourceNotifications.WaitForResourceHealthyAsync("gateway").WaitAsync(DefaultTimeout);

        ApiClient = App.CreateHttpClient("api");

        // Only the base address is needed — gateway tests drive the OAuth hops with their own handler.
        // Explicitly https: the cookie's SecurePolicy is Always and the OIDC handler's correlation
        // cookies default to SameAsRequest, so staying on https keeps every cookie sendable.
        using var gatewayProbe = App.CreateHttpClient("gateway", "https");
        GatewayBaseAddress = gatewayProbe.BaseAddress!;

        // The gateway's configured CORS origin. No health wait needed — the value depends only on the
        // "web" resource's known endpoint, not on it actually being up.
        using var webProbe = App.CreateHttpClient("web", "https");
        SpaOrigin = webProbe.BaseAddress!;
    }

    public async Task DisposeAsync()
    {
        ApiClient?.Dispose();
        await App.DisposeAsync();
    }
}

/// <summary>
/// The API with its real production anonymous rate limit, for the tests whose subject is the limit
/// itself. Everything else shares <see cref="AegisScribeApiCollection"/> and its raised bucket.
/// </summary>
public sealed class TightRateLimitAppFixture : AegisScribeAppFixture
{
    // Read from RateLimitOptions rather than typed as a literal, so the production default has exactly
    // one definition and this cannot drift away from it.
    protected override int AnonymousPermitLimit => new RateLimitOptions().AnonymousPermitLimit;
}

/// <summary>
/// The shared collection: one AppHost for every integration test that does not need its own. Making
/// the anonymous rate limit configurable (<see cref="RateLimitOptions"/>) is what allows one — the
/// collections were split only so their tests would not exhaust each other's bucket.
/// </summary>
[CollectionDefinition("AegisScribe API")]
public class AegisScribeApiCollection : ICollectionFixture<AegisScribeAppFixture>;
