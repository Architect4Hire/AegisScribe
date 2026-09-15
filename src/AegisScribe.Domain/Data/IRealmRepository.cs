using AegisScribe.Domain.Managers.Models.Domain;

namespace AegisScribe.Domain.Data;

// Realm reads and writes, split out of ICharacterRepository in 6.4b. The realm catalogue sync writes
// nothing but realms, and depending on a character repository to do it would have been the kind of
// seam that reads as an accident later.
public interface IRealmRepository
{
    Task<Realm?> FindAsync(string region, string realmSlug, CancellationToken ct);

    Task<IReadOnlyList<Realm>> ListByRegionAsync(string region, CancellationToken ct);

    // The catalogue pass, as one operation: load the region's realms, reconcile the fetched set against
    // them in memory, and stage the result. ~250 realms cost one query and one SaveChanges instead of
    // a find-then-update round trip each. Returns how many were new, which is only for the log line.
    //
    // Nothing is deleted. A realm missing from Blizzard's answer is far more likely to be a partial
    // pass than a realm that ceased to exist, and removing one would orphan every character on it.
    Task<int> UpsertCatalogueAsync(string region, IReadOnlyList<Realm> fresh, CancellationToken ct);

    // The newest LastSyncedAt in a region, or null when the region holds no realms. The catalogue
    // sync's freshness gate reads this to decide whether a pass is due at all — without it every
    // worker restart would re-fetch the catalogue, and `aspire run` restarts it constantly.
    Task<DateTimeOffset?> LatestSyncedAtAsync(string region, CancellationToken ct);

    // Stages an insert or an in-place update. Matched on BlizzardRealmId first so a renamed realm
    // moves rather than duplicating, then on (Region, Slug) so a row that predates the catalogue sync
    // — and therefore has no Blizzard id — is adopted instead of collided with.
    Task<Realm> UpsertAsync(Realm fresh, CancellationToken ct);

    Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken ct);
}
