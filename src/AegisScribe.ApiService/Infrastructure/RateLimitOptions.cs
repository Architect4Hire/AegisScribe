namespace AegisScribe.ApiService.Infrastructure;

// Bound from the "RateLimits" configuration section. The defaults below ARE the production values: a
// deployment changes nothing, and only the test host raises a bucket, because the anonymous limit is
// tight enough (backend.md → "The API's public edge") that integration tests sharing one API process
// starve each other.
//
// RateLimitOptionsTests pins these defaults, so "make the tests faster" can never quietly become
// "weaken the public edge".
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimits";

    // Anonymous callers, partitioned by IP. The tightest bucket on purpose: a public hostname is
    // scanned within hours, and anonymous character lookup is the cheapest thing to abuse.
    public int AnonymousPermitLimit { get; set; } = 20;

    // A signed-in user, partitioned by `sub`. Per user rather than per IP because thousands of mobile
    // clients share one carrier NAT, and a per-IP bucket tight enough to stop an attacker would
    // throttle a whole city (backend.md).
    public int AuthenticatedPermitLimit { get; set; } = 100;

    // A whole client fleet, partitioned by client_id.
    public int ClientPermitLimit { get; set; } = 1000;

    // The slug-check endpoint, which stacks on top of the global limiter.
    public int SlugCheckPermitLimit { get; set; } = 30;

    // The invitation preview and accept, which stack on top of the global limiter. Tighter than
    // slug-check because these are keyed by a SECRET: somebody legitimately following a link makes
    // one or two calls, and anything past a handful a minute is a script.
    public int InvitationTokenPermitLimit { get; set; } = 10;

    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);

    public bool IsValid =>
        AnonymousPermitLimit > 0
        && AuthenticatedPermitLimit > 0
        && ClientPermitLimit > 0
        && SlugCheckPermitLimit > 0
        && InvitationTokenPermitLimit > 0
        && Window > TimeSpan.Zero;
}
