namespace AegisScribe.Domain.Integration.Blizzard;

// Bound from the "Blizzard" configuration section. The credentials arrive as Aspire parameters, so they
// are absent on any machine without them — a supported state, not an error. Nothing here validates
// their presence; see BlizzardServiceCollectionExtensions for what is validated and why.
public sealed class BlizzardOptions
{
    public const string SectionName = "Blizzard";

    // Chooses both hosts (BlizzardHosts) and the suffix on every namespace below.
    public string Region { get; set; } = "us";

    // A query parameter on every request, never a header — Blizzard ignores Accept-Language. Fetching
    // English strings is not a translation layer, and CLAUDE.md puts i18n out of bounds.
    public string Locale { get; set; } = "en_US";

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    // Overrides for the regionally-derived hosts, so the base address comes from config rather than a
    // literal (external.md): a regional outage is a config change, and tests can point at a stub.
    public string? ApiBaseAddress { get; set; }

    public string? OAuthBaseAddress { get; set; }

    // Renew this far ahead of the stated expiry, so a request in flight never carries a token that
    // expires between our check and Blizzard's.
    public TimeSpan TokenRefreshSkew { get; set; } = TimeSpan.FromMinutes(5);

    // How long to wait after a failed mint, so a battle.net outage cannot become a tight request loop
    // against their token endpoint.
    public TimeSpan TokenFailureCooldown { get; set; } = TimeSpan.FromSeconds(30);

    // The sustained outbound rate, as tokens replenished per second. 9/s is 32,400 calls an hour — 90%
    // of the contractual 36,000, leaving headroom because the cap is enforced on Blizzard's accounting
    // rather than ours, and a second process could share this client id.
    public int SustainedCallsPerSecond { get; set; } = 9;

    // The bucket size, and so the largest burst possible in one instant. Kept under
    // BlizzardRateLimits.AssumedCallsPerSecond: bursting is what makes a lazy cache-first read feel
    // fast, but a burst above the per-second cap earns a 429 however healthy the hourly budget looks.
    public int BurstCallsPerSecond { get; set; } = 80;

    // How many callers may queue for a token. A sync job legitimately waits its turn; beyond this the
    // limiter denies the lease and the caller degrades rather than queueing without bound.
    public int MaxQueuedCalls { get; set; } = 200;

    // The longest a caller sits out a shared backoff window before giving up. Blizzard can name a
    // Retry-After of a minute or more, and blocking a user request that long is worse than telling it
    // the data is unavailable.
    public TimeSpan MaxThrottleWait { get; set; } = TimeSpan.FromSeconds(5);

    // Used when Blizzard sends a 429 with no Retry-After. Long enough to matter, short enough that a
    // spurious 429 doesn't stall the app.
    public TimeSpan DefaultThrottleBackoff { get; set; } = TimeSpan.FromSeconds(10);

    // Credentials are optional by design: with none configured the gateway degrades to a no-op and the
    // app runs on stored and seeded data (external.md). This says nothing about whether they are valid,
    // only that we have a pair to try.
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);

    // Every Blizzard request carries one of these three. Picking the wrong one returns a 404 that reads
    // like "this character doesn't exist" — the single most confusing failure mode in this integration —
    // so they are computed here rather than typed at each call site.
    public string StaticNamespace => $"static-{Region.ToLowerInvariant()}";

    public string DynamicNamespace => $"dynamic-{Region.ToLowerInvariant()}";

    public string ProfileNamespace => $"profile-{Region.ToLowerInvariant()}";

    public Uri ResolveApiBaseAddress() =>
        new(string.IsNullOrWhiteSpace(ApiBaseAddress) ? BlizzardHosts.ApiHost(Region) : ApiBaseAddress);

    public Uri ResolveOAuthBaseAddress() =>
        new(string.IsNullOrWhiteSpace(OAuthBaseAddress) ? BlizzardHosts.OAuthHost(Region) : OAuthBaseAddress);
}
