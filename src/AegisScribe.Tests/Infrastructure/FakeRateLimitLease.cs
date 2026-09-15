using System.Threading.RateLimiting;

namespace AegisScribe.Tests.Infrastructure;

// RateLimitLease is abstract and the framework's implementations are internal, so a test that stands in for
// the Blizzard limiter needs its own. Records disposal, because releasing the lease is part of the
// handler's contract and a leaked lease is invisible until the budget mysteriously runs dry.
public sealed class FakeRateLimitLease(bool isAcquired) : RateLimitLease
{
    public static FakeRateLimitLease Acquired() => new(true);

    public static FakeRateLimitLease Denied() => new(false);

    public override bool IsAcquired { get; } = isAcquired;

    public bool WasDisposed { get; private set; }

    public override IEnumerable<string> MetadataNames => [];

    public override bool TryGetMetadata(string metadataName, out object? metadata)
    {
        metadata = null;
        return false;
    }

    protected override void Dispose(bool disposing)
    {
        WasDisposed = true;
        base.Dispose(disposing);
    }
}
