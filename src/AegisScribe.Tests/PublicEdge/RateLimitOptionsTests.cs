using AegisScribe.ApiService.Infrastructure;

namespace AegisScribe.Tests.PublicEdge;

// The guard on making the rate limits configurable. Moving them out of code was done to make the test
// suite cheaper, which is exactly the motivation that quietly relaxes a security control. The API is
// publicly addressable (backend.md), so the defaults ARE the production posture and this pins every one
// — changing a value should require deciding to.
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

        // Tighter than slug-check, because this one is keyed by a SECRET: somebody legitimately
        // following an invitation link makes one or two calls, and anything past a handful a minute
        // is a script walking the token space.
        Assert.Equal(10, options.InvitationTokenPermitLimit);

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
