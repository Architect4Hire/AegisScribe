using Microsoft.Extensions.Options;

namespace AegisScribe.Domain.Integration.Blizzard;

// "Is this stored row old enough to be worth a Blizzard call?" — asked by the cache-first read in the
// DataLayer today and by the sync worker's stale-row query later, which is the reason it is an injected
// object rather than a comparison written out at each call site: two callers answering the same
// compliance question two ways is how one of them drifts past thirty days unnoticed.
public interface IBlizzardStalenessPolicy
{
    bool IsStale(DateTimeOffset lastSyncedAt);

    // The threshold itself, for the sync worker's "find rows approaching the deadline" query, which
    // needs a cutoff timestamp rather than a per-row predicate it cannot translate to SQL.
    DateTimeOffset StaleBefore();
}

public sealed class BlizzardStalenessPolicy(
    IOptions<BlizzardStalenessOptions> options,
    TimeProvider timeProvider) : IBlizzardStalenessPolicy
{
    public bool IsStale(DateTimeOffset lastSyncedAt) => lastSyncedAt < StaleBefore();

    public DateTimeOffset StaleBefore() =>
        timeProvider.GetUtcNow() - options.Value.CharacterRefreshAfter;
}
