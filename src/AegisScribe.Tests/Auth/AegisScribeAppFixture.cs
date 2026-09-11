using Aspire.Hosting;
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

[CollectionDefinition("AegisScribe API")]
public class AegisScribeApiCollection : ICollectionFixture<AegisScribeAppFixture>;
