using AegisScribe.Domain.Data;
using AegisScribe.Domain.Integration.Blizzard;
using AegisScribe.Domain.Managers.Models.Domain;
using Microsoft.Extensions.Options;

namespace AegisScribe.SyncWorker;

// 6.4b — fills Realm for every realm in a region, connected-realm grouping included.
//
// Runs GLOBALLY, outside any tenant, and that is the whole economics of it: realm data is identical for
// every community, so one pass serves all of them. Per-tenant would multiply ~100 calls by tenant count
// and breach the contractual cap on the third community (tenancy.md, external.md).
//
// Reaches Blizzard only through the gateway and writes only through the repository, per external.md's
// rules for the worker.
public sealed class RealmCatalogueSync(
    IBlizzardGateway gateway,
    IRealmRepository realms,
    IOptions<BlizzardOptions> blizzardOptions,
    IOptions<BlizzardStalenessOptions> stalenessOptions,
    TimeProvider timeProvider,
    ILogger<RealmCatalogueSync> logger)
{
    // Not a throughput control — the rate limiter already holds outbound calls to 9/second, so a pass of
    // ~100 connected realms is limiter-bound at roughly eleven seconds whatever this says.
    //
    // What it bounds is QUEUE OCCUPANCY. An unbounded fan-out parks ~100 leases in a queue 200 deep
    // (BlizzardOptions.MaxQueuedCalls), and a user's character lookup then waits behind the entire realm
    // catalogue. Four holds that to four.
    private const int MaxConcurrentConnectedRealmFetches = 4;

    public async Task<RealmCatalogueSyncResult> RunAsync(CancellationToken ct)
    {
        var region = blizzardOptions.Value.Region;

        if (!gateway.IsConfigured)
        {
            // Missing credentials degrade, they do not crash (external.md). The app runs on seeded data.
            logger.LogInformation(
                "Blizzard credentials are not configured; skipping the realm catalogue sync for {Region}.",
                region);

            return RealmCatalogueSyncResult.Skipped(region);
        }

        if (await IsCatalogueFreshAsync(region, ct))
        {
            // The restart gate. `aspire run` restarts this worker constantly in local development, and
            // without this every restart would spend ~100 contractual calls re-fetching a catalogue that
            // has not changed since breakfast.
            logger.LogInformation(
                "The realm catalogue for {Region} is inside the refresh window; skipping this pass.",
                region);

            return RealmCatalogueSyncResult.Skipped(region);
        }

        var connectedRealmIds = await gateway.FetchConnectedRealmIdsAsync(region, ct);

        if (connectedRealmIds.Count == 0)
        {
            logger.LogWarning("Blizzard returned no connected realms for {Region}.", region);
            return RealmCatalogueSyncResult.Skipped(region);
        }

        var fetched = await FetchAllAsync(region, connectedRealmIds, ct);

        if (fetched.Count == 0)
        {
            // Every group failed. Writing an empty pass would advance nothing and log a success that
            // did not happen.
            logger.LogWarning(
                "The realm catalogue pass for {Region} fetched no realms from {GroupCount} connected realms.",
                region,
                connectedRealmIds.Count);

            return RealmCatalogueSyncResult.Skipped(region);
        }

        // Every fetch is finished before the transaction opens. The callback is retryable — Aspire's SQL
        // Server integration enables retry-on-failure — and an HTTP call inside it would fire again on
        // every retry, spending contractual budget and rolled back by nothing.
        var inserted = await realms.ExecuteInTransactionAsync(
            ct => realms.UpsertCatalogueAsync(region, fetched, ct),
            ct);

        logger.LogInformation(
            "Realm catalogue for {Region}: {RealmCount} realms across {GroupCount} connected realms, " +
            "{InsertedCount} new.",
            region,
            fetched.Count,
            connectedRealmIds.Count,
            inserted);

        return new RealmCatalogueSyncResult(region, fetched.Count, connectedRealmIds.Count, inserted, Ran: true);
    }

    private async Task<bool> IsCatalogueFreshAsync(string region, CancellationToken ct)
    {
        var latest = await realms.LatestSyncedAtAsync(region, ct);

        // No realms at all means a first run, never "fresh". A region holding only realms that 6.4
        // resolved lazily is also due, because those are a handful of rows rather than a catalogue.
        return latest is { } lastSyncedAt
            && lastSyncedAt > timeProvider.GetUtcNow() - stalenessOptions.Value.RealmRefreshAfter;
    }

    private async Task<List<Realm>> FetchAllAsync(
        string region,
        IReadOnlyList<long> connectedRealmIds,
        CancellationToken ct)
    {
        var fetched = new List<Realm>();
        var gate = new Lock();
        var failures = 0;

        // Bounded, never Task.WhenAll over the whole list (external.md, add-external-sync step 6). A
        // guild roster taught this lesson first; a hundred connected realms would teach it again.
        await Parallel.ForEachAsync(
            connectedRealmIds,
            new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentConnectedRealmFetches, CancellationToken = ct },
            async (connectedRealmId, token) =>
            {
                try
                {
                    var group = await gateway.FetchConnectedRealmAsync(region, connectedRealmId, token);

                    lock (gate)
                    {
                        fetched.AddRange(group);
                    }
                }
                catch (BlizzardUnavailableException exception)
                {
                    // One group failing does not fail the pass. The realms we did get are still worth
                    // storing, and the next run picks up the rest — which is what makes this resumable
                    // without tracking progress anywhere.
                    lock (gate)
                    {
                        failures++;
                    }

                    logger.LogWarning(
                        exception,
                        "Could not fetch connected realm {ConnectedRealmId} for {Region}.",
                        connectedRealmId,
                        region);
                }
            });

        if (failures > 0)
        {
            logger.LogWarning(
                "{FailureCount} of {GroupCount} connected realms could not be fetched for {Region}; " +
                "the next pass will retry them.",
                failures,
                connectedRealmIds.Count,
                region);
        }

        return fetched;
    }
}

public sealed record RealmCatalogueSyncResult(
    string Region,
    int RealmCount,
    int ConnectedRealmCount,
    int InsertedCount,
    bool Ran)
{
    public static RealmCatalogueSyncResult Skipped(string region) => new(region, 0, 0, 0, Ran: false);
}
