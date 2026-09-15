using AegisScribe.Domain.Managers.Models.Domain;

namespace AegisScribe.Domain.Data;

// Realm reads and writes, separate from ICharacterRepository: the catalogue sync writes nothing but
// realms, and depending on a character repository to do it would read as an accident later.
public interface IRealmRepository
{
    Task<Realm?> FindAsync(string region, string realmSlug, CancellationToken ct);

    Task<IReadOnlyList<Realm>> ListByRegionAsync(string region, CancellationToken ct);

    // The catalogue pass as one operation: ~250 realms cost one query and one SaveChanges instead of a
    // find-then-update round trip each.
    //
    // Nothing is deleted. A realm missing from Blizzard's answer is far more likely to be a partial
    // pass than a realm that ceased to exist, and removing one would orphan every character on it.
    Task<int> UpsertCatalogueAsync(string region, IReadOnlyList<Realm> fresh, CancellationToken ct);

    // The catalogue sync's freshness gate. Without it every worker restart re-fetches the catalogue,
    // and `aspire run` restarts it constantly.
    Task<DateTimeOffset?> LatestSyncedAtAsync(string region, CancellationToken ct);

    // Stages an insert or an in-place update. Matched on BlizzardRealmId first so a renamed realm
    // moves rather than duplicating, then on (Region, Slug) so a row that predates the catalogue sync
    // — and therefore has no Blizzard id — is adopted instead of collided with.
    Task<Realm> UpsertAsync(Realm fresh, CancellationToken ct);

    Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken ct);
}
