using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AegisScribe.Domain.Integration.Blizzard;

public static class BlizzardServiceCollectionExtensions
{
    // Separate from AddAegisScribeDomain, because the hosts that need Blizzard are not the hosts that
    // need the request stack: the sync worker takes this without Identity, an ICurrentUser or a resolved
    // tenant.
    //
    // The host is still expected to have called AddServiceDefaults(), which is where retries, the
    // circuit breaker and timeouts for both clients below come from.
    public static IServiceCollection AddBlizzardIntegration(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddOptions<BlizzardOptions>()
            .BindConfiguration(BlizzardOptions.SectionName)
            // Shape only, and deliberately nothing about the credentials — offline development is a
            // first-class case and the gateway degrades to a no-op (external.md). A region we have no
            // hosts for IS a genuine misconfiguration, and should stop startup rather than 404 on every
            // call later.
            .Validate(
                options => BlizzardHosts.IsSupported(options.Region),
                $"Blizzard:Region must be one of: {string.Join(", ", BlizzardHosts.SupportedRegions)}.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.Locale),
                "Blizzard:Locale must be set — it is a required query parameter on every Blizzard request.")
            .Validate(
                options => options.TokenRefreshSkew > TimeSpan.Zero,
                "Blizzard:TokenRefreshSkew must be positive.")
            .Validate(
                options => options.TokenFailureCooldown > TimeSpan.Zero,
                "Blizzard:TokenFailureCooldown must be positive.")
            .Validate(
                options => options.SustainedCallsPerSecond > 0 && options.BurstCallsPerSecond > 0,
                "Blizzard:SustainedCallsPerSecond and Blizzard:BurstCallsPerSecond must both be positive.")
            .Validate(
                options => options.BurstCallsPerSecond >= options.SustainedCallsPerSecond,
                "Blizzard:BurstCallsPerSecond must be at least Blizzard:SustainedCallsPerSecond — a bucket " +
                "smaller than one period's replenishment throws away budget it can never hold.")
            .Validate(
                options => options.BurstCallsPerSecond <= BlizzardRateLimits.AssumedCallsPerSecond,
                $"Blizzard:BurstCallsPerSecond must not exceed the per-second cap of " +
                $"{BlizzardRateLimits.AssumedCallsPerSecond} calls/second.")
            // The contractual one: 36,000 calls an hour is a term of the Developer API Terms of Use, not
            // a tuning knob with a sensible range, so a configuration that could exceed it must stop
            // startup. There is a test on this too — it is the setting most likely to be raised by
            // someone chasing sync throughput.
            .Validate(
                options => BlizzardRateLimits.WorstCaseCallsPerHour(
                    options.SustainedCallsPerSecond,
                    options.BurstCallsPerSecond) <= BlizzardRateLimits.ContractualCallsPerHour,
                $"Blizzard:SustainedCallsPerSecond and Blizzard:BurstCallsPerSecond together allow more than " +
                $"the contractual {BlizzardRateLimits.ContractualCallsPerHour} Blizzard calls per hour.")
            .Validate(
                options => options.MaxQueuedCalls >= 0,
                "Blizzard:MaxQueuedCalls must not be negative.")
            .Validate(
                options => options.MaxThrottleWait > TimeSpan.Zero && options.DefaultThrottleBackoff > TimeSpan.Zero,
                "Blizzard:MaxThrottleWait and Blizzard:DefaultThrottleBackoff must both be positive.")
            .ValidateOnStart();

        services.AddOptions<BlizzardStalenessOptions>()
            .BindConfiguration(BlizzardStalenessOptions.SectionName)
            // The other contractual one, and the reason this section is validated at all. The Terms of
            // Use permit storing Blizzard data only if it is refreshed at least every thirty days, so a
            // configuration that would let a row age past that is a breach rather than a slow cache.
            .Validate(
                options => options.CharacterRefreshAfter <= BlizzardStalenessOptions.MaximumRefreshInterval,
                $"Blizzard:Staleness:CharacterRefreshAfter must not exceed " +
                $"{BlizzardStalenessOptions.MaximumRefreshInterval.TotalDays} days — the Blizzard " +
                $"Developer API Terms of Use require stored data to be refreshed at least that often.")
            .Validate(
                options => options.CharacterRefreshAfter > TimeSpan.Zero,
                "Blizzard:Staleness:CharacterRefreshAfter must be positive — a zero or negative window " +
                "makes every read a Blizzard call and spends the hourly budget on data already stored.")
            .Validate(
                options => options.RealmRefreshAfter <= BlizzardStalenessOptions.MaximumRefreshInterval,
                $"Blizzard:Staleness:RealmRefreshAfter must not exceed " +
                $"{BlizzardStalenessOptions.MaximumRefreshInterval.TotalDays} days — the Blizzard " +
                $"Developer API Terms of Use require stored data to be refreshed at least that often.")
            .Validate(
                options => options.RealmRefreshAfter > TimeSpan.Zero,
                "Blizzard:Staleness:RealmRefreshAfter must be positive — a zero or negative window makes " +
                "every worker restart re-fetch the whole realm catalogue.")
            .ValidateOnStart();

        // Singleton: it holds only the options snapshot and the clock, and both the request path and the
        // sync worker ask it the same question.
        services.TryAddSingleton<IBlizzardStalenessPolicy, BlizzardStalenessPolicy>();

        // Both singletons: a per-scope token cache would mint a token per request, and a per-scope
        // availability cache would probe Blizzard on every health poll.
        services.TryAddSingleton<IBlizzardTokenProvider, BlizzardTokenProvider>();
        services.TryAddSingleton<BlizzardAvailabilityCache>();

        // One limiter for the whole process — see BlizzardRateLimiter for why it is a token bucket.
        services.TryAddSingleton<IBlizzardRateLimiter>(provider =>
        {
            var blizzardOptions = provider.GetRequiredService<IOptions<BlizzardOptions>>();

            return new BlizzardRateLimiter(
                BlizzardRateLimiter.CreateLimiter(blizzardOptions.Value),
                blizzardOptions,
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<ILogger<BlizzardRateLimiter>>());
        });

        services.AddTransient<BlizzardAuthHandler>();
        services.AddTransient<BlizzardRateLimitHandler>();

        // Says once, at startup, whether Blizzard is configured — see BlizzardStartupLogger for why this
        // cannot live on a code path a request reaches.
        services.AddHostedService<BlizzardStartupLogger>();

        // The token endpoint's own client. Note the absence of both handlers: the 36,000/hour cap is on
        // the Developer API host, not on battle.net's token endpoint, and the single-flight mint in
        // BlizzardTokenProvider already makes these calls roughly daily.
        services.AddHttpClient(BlizzardDefaults.OAuthHttpClientName, ConfigureOAuthClient);

        // Handler order is outermost-first, and it matters. Auth goes first because it can short-circuit
        // when there are no credentials, and an unconfigured deployment should not spend budget
        // discovering that. The rate limiter goes last so it sits immediately before the wire, where it
        // sees every real attempt — including the retries the resilience handler makes on a 429.
        services.AddHttpClient<IBlizzardGateway, BlizzardGateway>(ConfigureApiClient)
            .AddHttpMessageHandler<BlizzardAuthHandler>()
            .AddHttpMessageHandler<BlizzardRateLimitHandler>();

        return services;
    }

    private static void ConfigureOAuthClient(IServiceProvider provider, HttpClient client)
    {
        var settings = provider.GetRequiredService<IOptions<BlizzardOptions>>().Value;

        // The REGIONAL OAuth host, not oauth.battle.net — see BlizzardHosts.
        client.BaseAddress = settings.ResolveOAuthBaseAddress();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private static void ConfigureApiClient(IServiceProvider provider, HttpClient client)
    {
        var settings = provider.GetRequiredService<IOptions<BlizzardOptions>>().Value;

        client.BaseAddress = settings.ResolveApiBaseAddress();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }
}
