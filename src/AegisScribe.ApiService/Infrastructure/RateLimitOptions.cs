namespace AegisScribe.ApiService.Infrastructure;

// Bound from the "RateLimits" configuration section.
//
// These were literals in Program.cs until the test suite made the cost visible: the anonymous bucket is
// deliberately tight (backend.md -> "The API's public edge"), tight enough that a handful of integration
// tests sharing one API process starve each other. The workaround had been to give almost every test
// collection its own AppHost — nine of them — which is most of what a test run spent its time on.
//
// So the numbers move to configuration and the defaults below ARE the production values: absent any
// configuration this class reproduces exactly what the literals did. A deployment changes nothing; the
// test host raises one bucket. RateLimitOptionsTests pins the defaults so that "make the tests faster"
// can never quietly become "weaken the public edge".
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

    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);

    public bool IsValid =>
        AnonymousPermitLimit > 0
        && AuthenticatedPermitLimit > 0
        && ClientPermitLimit > 0
        && SlugCheckPermitLimit > 0
        && Window > TimeSpan.Zero;
}
