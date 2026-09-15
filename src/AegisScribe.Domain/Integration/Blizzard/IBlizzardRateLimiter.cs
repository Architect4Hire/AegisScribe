using System.Threading.RateLimiting;

namespace AegisScribe.Domain.Integration.Blizzard;

// The one shared limiter in front of the Blizzard gateway (external.md -> "Rate limiting is not
// optional"). Shared is the operative word: 36,000 calls an hour is a budget for the whole process, so
// every outbound call draws on this one instance.
public interface IBlizzardRateLimiter
{
    // Waits for permission to make one Blizzard call. Dispose the lease when the call is done.
    //
    // A lease with IsAcquired == false means "give up and degrade" — the queue is full, or a shared backoff
    // window is longer than we are willing to wait. It is never an exception, because a throttled Blizzard
    // is a normal condition this app is required to survive.
    Task<RateLimitLease> AcquireAsync(CancellationToken cancellationToken);

    // Blizzard said 429. Every caller backs off until the hint passes, not just the one that was refused —
    // otherwise the other callers in flight spend the next second proving the same point.
    void RegisterThrottled(TimeSpan? retryAfter);
}
