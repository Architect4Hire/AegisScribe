using System.Globalization;
using AegisScribe.ApiService.Infrastructure;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AegisScribe.Tests.Auth;

/// <summary>
/// Boots the real AppHost once for the collection so integration tests exercise the actual
/// distributed app (real SQL Server, real Identity wiring) rather than a fake. The SQL/Redis
/// containers are ContainerLifetime.Persistent, so this attaches to whatever is already running
/// locally instead of spinning up new ones.
/// </summary>
public class AegisScribeAppFixture : IAsyncLifetime
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    /// The anonymous-IP permit limit this fixture's API runs with.
    /// <para>
    /// Production is 20 a minute, deliberately tight (backend.md → "The API's public edge"). That is
    /// far below what a suite of integration tests needs, and the cost used to be paid in AppHost
    /// instances: almost every collection had its own so the tests would stop starving each other's
    /// bucket. Nine full distributed apps per run, most of a test run's wall clock, and enough
    /// contention between them to produce rotating timeout failures.
    /// </para>
    /// <para>
    /// Raising it here instead buys all of that back. The limiter is still wired, still partitioned by
    /// IP, still returns 429 with Retry-After — only the number differs, and
    /// <see cref="TightRateLimitAppFixture"/> keeps the production number for the tests whose subject
    /// IS the limit.
    /// </para>
    /// </summary>
    protected virtual int AnonymousPermitLimit => 10_000;

    /// <summary>
    /// The per-tenant sync budget this fixture's API runs with, in Blizzard calls per window.
    /// <para>
    /// Production is 2,000 — roughly two full re-syncs of a very large guild. Exhausting that over HTTP
    /// would take a thousand requests, so the tests run against a budget of four: two character
    /// refreshes at two calls each, and the third request is the 429. The mechanism under test is
    /// identical; only the number it counts to differs.
    /// </para>
    /// </summary>
    public const int SyncBudgetCallsPerWindow = 4;

    /// <summary>Calls one character refresh costs: the summary and the equipment.</summary>
    public const int CallsPerCharacterRefresh = 2;

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

        // The migration service seeds aegisscribe-ops with this same plaintext value (from the
        // identical Aspire parameter/user-secrets store), so this is the secret that mints a real token.
        OpsClientSecret = appHost.Configuration["Parameters:ops-client-secret"]
            ?? throw new InvalidOperationException("ops-client-secret parameter not configured.");

        // The migration service seeds aegisscribe-bff with this same value too — 1B.9's edge
        // verification drives a direct code+PKCE exchange as this client to reach a real refresh
        // token, since the gateway's own session never exposes one to a caller.
        const string bffParamPrefix = "Parameters:bff-client-";
        const string bffParamSuffix = "secret";
        BffSecret
            = appHost.Configuration[bffParamPrefix + bffParamSuffix]
            ?? throw new InvalidOperationException("bff client parameter not configured.");

        var api = appHost.Resources.OfType<ProjectResource>().Single(resource => resource.Name == "api");
        appHost.CreateResourceBuilder(api)
            .WithEnvironment(
                "RateLimits__AnonymousPermitLimit",
                AnonymousPermitLimit.ToString(CultureInfo.InvariantCulture))
            .WithEnvironment(
                "TenantSyncBudget__CallsPerWindow",
                SyncBudgetCallsPerWindow.ToString(CultureInfo.InvariantCulture));

        // The sync worker is not under test from here, and it is not free: it connects to SQL and runs
        // its Blizzard passes on startup. Nothing references it, so dropping it costs a process per
        // AppHost boot and loses no coverage — CharacterRefreshSyncTests and RealmCatalogueSyncTests
        // exercise those jobs directly.
        //
        // `web` stays, unlike sync: gateway.WithReference(web) supplies the CORS origin, and this
        // fixture resolves SpaOrigin from it. It never has to become healthy, only exist.
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

        // Only the base address is needed — gateway tests drive the OAuth hops by hand with their
        // own HttpClientHandler (cookie container, redirects disabled), not this resilience-wrapped
        // client. Explicitly https: the cookie's SecurePolicy is Always, and the OIDC handler's own
        // correlation/nonce cookies default to SameAsRequest — staying on https end to end keeps
        // every cookie in the exchange actually sendable back by a real cookie container.
        using var gatewayProbe = App.CreateHttpClient("gateway", "https");
        GatewayBaseAddress = gatewayProbe.BaseAddress!;

        // The gateway's configured CORS origin (1B.8) — resolved the same way, no health wait
        // needed since the value only depends on the "web" resource's known endpoint, not on it
        // actually being up.
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
/// The shared collection: one AppHost for every integration test that does not need its own.
/// <para>
/// It used to be nine. Each collection got its own distributed app purely so its tests would not
/// exhaust the API's 20-a-minute anonymous bucket, which meant every run paid for nine sets of
/// migrations, APIs and gateways, all racing each other over one SQL container. Making the limit
/// configurable (<see cref="RateLimitOptions"/>) removed the reason, and merging them removes the cost.
/// </para>
/// </summary>
[CollectionDefinition("AegisScribe API")]
public class AegisScribeApiCollection : ICollectionFixture<AegisScribeAppFixture>;
