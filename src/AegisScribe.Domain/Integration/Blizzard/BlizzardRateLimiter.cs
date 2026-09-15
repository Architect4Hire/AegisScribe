using System.Threading.RateLimiting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AegisScribe.Domain.Integration.Blizzard;

// A token bucket plus a shared backoff window. Singleton — a per-scope limiter would hand every request
// its own 36,000/hour budget, which is the same as having none.
//
// Why a token bucket rather than a fixed window: it expresses both of Blizzard's caps at once. The
// replenishment rate holds the hourly total under the contractual 36,000, and the bucket size caps the
// largest possible instantaneous burst under the per-second limit. A fixed hourly window would permit
// 36,000 calls in the first second of the hour and satisfy the contract while earning a 429 for every one
// of them.
public sealed class BlizzardRateLimiter : IBlizzardRateLimiter, IAsyncDisposable
{
    private readonly RateLimiter _limiter;
    private readonly IOptions<BlizzardOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<BlizzardRateLimiter> _logger;

    // DateTimeOffset.UtcTicks of the end of the current shared backoff window. Read and written with
    // Interlocked because every outbound call touches it.
    private long _throttledUntilTicks;

    public BlizzardRateLimiter(
        RateLimiter limiter,
        IOptions<BlizzardOptions> options,
        TimeProvider timeProvider,
        ILogger<BlizzardRateLimiter> logger)
    {
        _limiter = limiter;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    // The production limiter. Separate from the constructor so tests can supply a bucket with
    // AutoReplenishment turned off and drive TryReplenish() by hand, which is the only way to assert
    // replenishment behaviour without sleeping.
    public static RateLimiter CreateLimiter(BlizzardOptions options) =>
        new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = options.BurstCallsPerSecond,
            TokensPerPeriod = options.SustainedCallsPerSecond,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            QueueLimit = options.MaxQueuedCalls,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true,
        });

    public async Task<RateLimitLease> AcquireAsync(CancellationToken cancellationToken)
    {
        var settings = _options.Value;
        var remaining = RemainingThrottle();

        if (remaining > TimeSpan.Zero)
        {
            if (remaining > settings.MaxThrottleWait)
            {
                // Sitting out a long Retry-After inside a request is worse than telling the caller the data
                // is unavailable: the caller degrades to stored data immediately, and the sync worker picks
                // the work up on its next pass.
                _logger.LogWarning(
                    "Skipping a Blizzard call: backing off for another {Remaining} after a 429, which is longer than the {MaxThrottleWait} this caller will wait.",
                    remaining,
                    settings.MaxThrottleWait);

                return ThrottledLease.Instance;
            }

            await Task.Delay(remaining, _timeProvider, cancellationToken);
        }

        return await _limiter.AcquireAsync(permitCount: 1, cancellationToken);
    }

    public void RegisterThrottled(TimeSpan? retryAfter)
    {
        var settings = _options.Value;
        var backoff = retryAfter is { } hint && hint > TimeSpan.Zero ? hint : settings.DefaultThrottleBackoff;
        var until = (_timeProvider.GetUtcNow() + backoff).UtcTicks;

        // Only ever extend the window. Two concurrent 429s must not let the shorter hint shorten the
        // longer one, and a late-arriving response carrying an old hint must not cut a newer window short.
        var current = Interlocked.Read(ref _throttledUntilTicks);

        while (until > current)
        {
            var observed = Interlocked.CompareExchange(ref _throttledUntilTicks, until, current);

            if (observed == current)
            {
                _logger.LogWarning(
                    "Blizzard returned 429. Backing off every Blizzard call for {Backoff}{Hinted}.",
                    backoff,
                    retryAfter is null ? " (no Retry-After header; using the configured default)" : " (from Retry-After)");
                return;
            }

            current = observed;
        }
    }

    public ValueTask DisposeAsync() => _limiter.DisposeAsync();

    private TimeSpan RemainingThrottle()
    {
        var until = Interlocked.Read(ref _throttledUntilTicks);

        if (until == 0)
        {
            return TimeSpan.Zero;
        }

        var remaining = new DateTimeOffset(until, TimeSpan.Zero) - _timeProvider.GetUtcNow();

        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    // RateLimitLease is abstract and the framework's "not acquired" implementations are internal, so the
    // refusal needs its own type.
    private sealed class ThrottledLease : RateLimitLease
    {
        public static readonly ThrottledLease Instance = new();

        public override bool IsAcquired => false;

        public override IEnumerable<string> MetadataNames => [];

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            metadata = null;
            return false;
        }
    }
}
