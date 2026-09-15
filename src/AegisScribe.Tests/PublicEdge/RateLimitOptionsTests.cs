using AegisScribe.ApiService.Infrastructure;

namespace AegisScribe.Tests.PublicEdge;

// The guard on making the rate limits configurable.
//
// Those numbers were literals in Program.cs, and moving them to configuration was done to make the test
// suite cheaper — which is exactly the kind of motivation that quietly relaxes a security control. The
// API is publicly addressable (backend.md → "The API's public edge"), so the defaults ARE the production
// posture and this pins every one of them. Changing a value here should require deciding to.
public class RateLimitOptionsTests
{
    [Fact]
    public void TheDefaultsAreTheProductionNumbers()
    {
        var options = new RateLimitOptions();

        // The tightest bucket, and the one the test fixture raises. A public hostname is scanned within
        // hours of DNS propagating, and anonymous character lookup is the cheapest thing to abuse.
        Assert.Equal(20, options.AnonymousPermitLimit);

        // Per user rather than per IP: thousands of mobile clients share one carrier NAT, so a per-IP
        // bucket tight enough to stop an attacker would throttle a whole city.
        Assert.Equal(100, options.AuthenticatedPermitLimit);

        Assert.Equal(1000, options.ClientPermitLimit);
        Assert.Equal(30, options.SlugCheckPermitLimit);
        Assert.Equal(TimeSpan.FromMinutes(1), options.Window);
    }

    [Fact]
    public void AnUnconfiguredApiGetsTheProductionPosture()
    {
        // Program.cs falls back to `new RateLimitOptions()` when the section is absent, so a deployment
        // that configures nothing is limited exactly as it was before the section existed.
        Assert.True(new RateLimitOptions().IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ANonPositivePermitLimitIsInvalid(int permitLimit)
    {
        // Zero does not mean "unlimited" to a FixedWindowRateLimiter, it means "reject everything" — so
        // this has to fail startup rather than be read as a way to turn the limiter off.
        Assert.False(new RateLimitOptions { AnonymousPermitLimit = permitLimit }.IsValid);
        Assert.False(new RateLimitOptions { AuthenticatedPermitLimit = permitLimit }.IsValid);
        Assert.False(new RateLimitOptions { ClientPermitLimit = permitLimit }.IsValid);
        Assert.False(new RateLimitOptions { SlugCheckPermitLimit = permitLimit }.IsValid);
    }

    [Fact]
    public void ANonPositiveWindowIsInvalid()
    {
        Assert.False(new RateLimitOptions { Window = TimeSpan.Zero }.IsValid);
        Assert.False(new RateLimitOptions { Window = TimeSpan.FromMinutes(-1) }.IsValid);
    }
}
