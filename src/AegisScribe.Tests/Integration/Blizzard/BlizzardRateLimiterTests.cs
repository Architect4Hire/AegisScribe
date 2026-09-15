using System.Threading.RateLimiting;
using AegisScribe.Domain.Integration.Blizzard;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace AegisScribe.Tests.Integration.Blizzard;

// 6.2 — the one shared limiter in front of the gateway (.claude/rules/external.md -> "Rate limiting is not
// optional"; references/blizzard-terms-and-limits.md -> "Rate limits").
//
// Two behaviours are under test and they are separate concerns: the token bucket enforcing the burst and
// sustained caps, and the shared backoff window that a 429 opens for every caller at once.
public class BlizzardRateLimiterTests
{
    [Fact]
    public async Task Acquire_WithBudgetAvailable_LeasesImmediately()
    {
        var limiter = Create(out _, tokenLimit: 5, queueLimit: 0);

        using var lease = await limiter.AcquireAsync(CancellationToken.None);

        Assert.True(lease.IsAcquired);
    }

    [Fact]
    public async Task Acquire_UpToTheBurstLimit_AllSucceed_AndTheNextIsRefused()
    {
        // The bucket size is the largest burst Blizzard can see from us in one instant. Beyond it the caller
        // is refused rather than queued here (queueLimit 0), which is the shape a request-path caller wants:
        // degrade to stored data now, don't hold the request open.
        var limiter = Create(out _, tokenLimit: 3, queueLimit: 0);

        var leases = new List<RateLimitLease>();

        for (var i = 0; i < 3; i++)
        {
            leases.Add(await limiter.AcquireAsync(CancellationToken.None));
        }

        Assert.All(leases, lease => Assert.True(lease.IsAcquired));

        using var refused = await limiter.AcquireAsync(CancellationToken.None);
        Assert.False(refused.IsAcquired);

        foreach (var lease in leases)
        {
            lease.Dispose();
        }
    }

    [Fact]
    public async Task Acquire_AfterTheBucketRefills_SucceedsAgain()
    {
        // The budget has to recover, or one exhausted bucket would wedge Blizzard reads until a restart.
        //
        // The one test here using the real clock, and not for want of trying: TokenBucketRateLimiter
        // takes no TimeProvider, and TryReplenish() adds tokens proportional to elapsed Stopwatch time
        // rather than a flat amount per call. Hence a very short period and a real delay several times
        // longer, which makes the margin generous — a slower machine only elapses more time, never less.
        var replenishmentPeriod = TimeSpan.FromMilliseconds(10);

        var limiter = Create(
            out _,
            tokenLimit: 2,
            queueLimit: 0,
            tokensPerPeriod: 2,
            autoReplenishment: false,
            out var bucket,
            replenishmentPeriod: replenishmentPeriod);

        using (await limiter.AcquireAsync(CancellationToken.None))
        using (await limiter.AcquireAsync(CancellationToken.None))
        {
            using var refused = await limiter.AcquireAsync(CancellationToken.None);
            Assert.False(refused.IsAcquired);
        }

        await Task.Delay(replenishmentPeriod * 6);
        bucket.TryReplenish();

        using var afterRefill = await limiter.AcquireAsync(CancellationToken.None);
        Assert.True(afterRefill.IsAcquired);
    }

    [Fact]
    public async Task RegisterThrottled_WithinTheWaitBudget_MakesCallersWaitOutTheHint()
    {
        // The 429 backoff is shared, not per-caller: a caller that never saw the 429 still waits, because
        // otherwise the other requests in flight spend the next second proving the same point.
        var limiter = Create(out var time, tokenLimit: 5, queueLimit: 0,
            configure: options => options.MaxThrottleWait = TimeSpan.FromSeconds(30));

        limiter.RegisterThrottled(TimeSpan.FromSeconds(3));

        var acquiring = limiter.AcquireAsync(CancellationToken.None);
        Assert.False(acquiring.IsCompleted);

        time.Advance(TimeSpan.FromSeconds(3));

        using var lease = await acquiring;
        Assert.True(lease.IsAcquired);
    }

    [Fact]
    public async Task RegisterThrottled_WithAHintLongerThanWeWillWait_RefusesTheLease()
    {
        // Blizzard can name a minute. Blocking a user request that long is worse than telling it the data is
        // unavailable — and the sync worker comes back around regardless.
        var limiter = Create(out _, tokenLimit: 5, queueLimit: 0,
            configure: options => options.MaxThrottleWait = TimeSpan.FromSeconds(5));

        limiter.RegisterThrottled(TimeSpan.FromMinutes(1));

        using var lease = await limiter.AcquireAsync(CancellationToken.None);

        Assert.False(lease.IsAcquired);
    }

    [Fact]
    public async Task RegisterThrottled_OnceTheWindowPasses_LeasesAgain()
    {
        var limiter = Create(out var time, tokenLimit: 5, queueLimit: 0,
            configure: options => options.MaxThrottleWait = TimeSpan.FromSeconds(5));

        limiter.RegisterThrottled(TimeSpan.FromMinutes(1));
        Assert.False((await limiter.AcquireAsync(CancellationToken.None)).IsAcquired);

        time.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1));

        using var lease = await limiter.AcquireAsync(CancellationToken.None);
        Assert.True(lease.IsAcquired);
    }

    [Fact]
    public async Task RegisterThrottled_ASecondShorterHint_DoesNotShortenTheWindow()
    {
        // Two concurrent 429s arrive in whatever order the network gives them. The shorter hint must not cut
        // the longer window short, or we go straight back into Blizzard's face.
        var limiter = Create(out _, tokenLimit: 5, queueLimit: 0,
            configure: options => options.MaxThrottleWait = TimeSpan.FromSeconds(5));

        limiter.RegisterThrottled(TimeSpan.FromMinutes(1));
        limiter.RegisterThrottled(TimeSpan.FromSeconds(1));

        using var lease = await limiter.AcquireAsync(CancellationToken.None);

        Assert.False(lease.IsAcquired);
    }

    [Fact]
    public async Task RegisterThrottled_WithNoHint_UsesTheConfiguredDefaultBackoff()
    {
        var limiter = Create(out var time, tokenLimit: 5, queueLimit: 0, configure: options =>
        {
            options.MaxThrottleWait = TimeSpan.FromSeconds(1);
            options.DefaultThrottleBackoff = TimeSpan.FromSeconds(20);
        });

        limiter.RegisterThrottled(retryAfter: null);

        Assert.False((await limiter.AcquireAsync(CancellationToken.None)).IsAcquired);

        time.Advance(TimeSpan.FromSeconds(21));

        using var lease = await limiter.AcquireAsync(CancellationToken.None);
        Assert.True(lease.IsAcquired);
    }

    [Fact]
    public async Task Acquire_WithACancelledToken_DoesNotConsumeBudget()
    {
        var limiter = Create(out _, tokenLimit: 1, queueLimit: 0);

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => limiter.AcquireAsync(cancelled.Token));

        // The single token is still there for a caller that has not given up.
        using var lease = await limiter.AcquireAsync(CancellationToken.None);
        Assert.True(lease.IsAcquired);
    }

    private static BlizzardRateLimiter Create(
        out FakeTimeProvider timeProvider,
        int tokenLimit,
        int queueLimit,
        Action<BlizzardOptions>? configure = null) =>
        Create(out timeProvider, tokenLimit, queueLimit, tokenLimit, true, out _, configure);

    private static BlizzardRateLimiter Create(
        out FakeTimeProvider timeProvider,
        int tokenLimit,
        int queueLimit,
        int tokensPerPeriod,
        bool autoReplenishment,
        out TokenBucketRateLimiter bucket,
        Action<BlizzardOptions>? configure = null,
        TimeSpan? replenishmentPeriod = null)
    {
        var options = new BlizzardOptions();
        configure?.Invoke(options);

        timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2026-09-13T12:00:00Z"));

        bucket = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = tokenLimit,
            TokensPerPeriod = tokensPerPeriod,
            ReplenishmentPeriod = replenishmentPeriod ?? TimeSpan.FromSeconds(1),
            QueueLimit = queueLimit,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = autoReplenishment,
        });

        return new BlizzardRateLimiter(
            bucket,
            new OptionsWrapper<BlizzardOptions>(options),
            timeProvider,
            NullLogger<BlizzardRateLimiter>.Instance);
    }
}
