namespace AegisScribe.Domain.Integration.Blizzard;

// Bound from the "Blizzard" configuration section. ClientId and ClientSecret arrive as Aspire
// parameters (Blizzard__ClientId / Blizzard__ClientSecret), so they are absent on any machine without
// credentials — which is a supported state, not an error. Nothing here validates their presence; see
// BlizzardServiceCollectionExtensions for what is and is not validated, and why.
public sealed class BlizzardOptions
{
    public const string SectionName = "Blizzard";

    // Chooses both hosts (BlizzardHosts) and the suffix on every namespace below.
    public string Region { get; set; } = "us";

    // A query parameter on every request, never a header — Blizzard ignores Accept-Language
    // (references/blizzard-endpoints.md). Fetching English strings is not a translation layer, and
    // CLAUDE.md puts i18n out of bounds.
    public string Locale { get; set; } = "en_US";

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    // Overrides for the regionally-derived hosts, so external.md's "the base address comes from config,
    // not a literal" holds: a regional outage is a config change rather than a redeploy, and tests can
    // point the clients at a stub.
    public string? ApiBaseAddress { get; set; }

    public string? OAuthBaseAddress { get; set; }

    // We renew this far ahead of the token's stated expiry, so a request in flight never carries a
    // token that expires between our check and Blizzard's.
    public TimeSpan TokenRefreshSkew { get; set; } = TimeSpan.FromMinutes(5);

    // How long to wait after a failed mint before trying again, so a battle.net outage cannot turn into
    // a tight request loop against their token endpoint.
    public TimeSpan TokenFailureCooldown { get; set; } = TimeSpan.FromSeconds(30);

    // The sustained outbound rate, as tokens replenished per second. 9/s is 32,400 calls an hour — 90% of
    // the contractual 36,000, leaving headroom for the fact that the cap is enforced on Blizzard's
    // accounting rather than ours, and that a second process could share this client id.
    public int SustainedCallsPerSecond { get; set; } = 9;

    // The bucket size, and therefore the largest burst possible in one instant. Kept under
    // BlizzardRateLimits.AssumedCallsPerSecond: bursting is what makes a lazy cache-first read feel fast,
    // but a burst above the per-second cap earns a 429 no matter how healthy the hourly budget looks.
    public int BurstCallsPerSecond { get; set; } = 80;

    // How many callers may queue for a token. A sync job legitimately wants to wait its turn; beyond this
    // the limiter denies the lease and the caller degrades rather than queueing without bound.
    public int MaxQueuedCalls { get; set; } = 200;

    // The longest a caller will sit out a shared backoff window before giving up. Blizzard can name a
    // Retry-After of a minute or more; blocking a user request that long is worse than telling it the data
    // is unavailable, and the sync worker will come back around anyway.
    public TimeSpan MaxThrottleWait { get; set; } = TimeSpan.FromSeconds(5);

    // Used when Blizzard sends a 429 with no Retry-After to go on. Long enough to matter, short enough
    // that a spurious 429 doesn't stall the app.
    public TimeSpan DefaultThrottleBackoff { get; set; } = TimeSpan.FromSeconds(10);

    // Credentials are optional by design: with none configured the gateway degrades to a no-op and the
    // app runs on stored and seeded data (external.md -> "Missing credentials are a no-op, not a
    // crash"). This says nothing about whether the credentials are valid — only that we have a pair to
    // try.
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);

    // Every Blizzard request carries one of these three. Picking the wrong one returns a 404 that reads
    // like "this character/guild doesn't exist", which references/blizzard-endpoints.md calls the single
    // most confusing failure mode in this integration — so they are computed here rather than typed at
    // each call site.
    public string StaticNamespace => $"static-{Region.ToLowerInvariant()}";

    public string DynamicNamespace => $"dynamic-{Region.ToLowerInvariant()}";

    public string ProfileNamespace => $"profile-{Region.ToLowerInvariant()}";

    public Uri ResolveApiBaseAddress() =>
        new(string.IsNullOrWhiteSpace(ApiBaseAddress) ? BlizzardHosts.ApiHost(Region) : ApiBaseAddress);

    public Uri ResolveOAuthBaseAddress() =>
        new(string.IsNullOrWhiteSpace(OAuthBaseAddress) ? BlizzardHosts.OAuthHost(Region) : OAuthBaseAddress);
}
