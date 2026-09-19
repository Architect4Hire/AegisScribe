using AegisScribe.Domain.Data;
using AegisScribe.Domain.Integration.Blizzard;
using Microsoft.Extensions.Options;

namespace AegisScribe.SyncWorker;

// The eager half of the refresh story. The DataLayer's cache-first read refreshes what somebody asked
// for; this refreshes what nobody asked for and the Terms of Use require anyway — every stored row
// re-fetched at least every thirty days, so a character nobody visits does not age out silently.
//
// It runs GLOBALLY, outside any tenant, and never iterates tenants. Character and CharacterEquipment
// are global reference data (tenancy.md), so refreshing per tenant would multiply this job's API calls
// by tenant count and breach the contractual cap on the third community.
public sealed class CharacterRefreshSync(
    IServiceScopeFactory scopeFactory,
    IBlizzardStalenessPolicy staleness,
    IOptions<SyncWorkerOptions> options,
    ILogger<CharacterRefreshSync> logger)
{
    public async Task<CharacterRefreshResult> RunAsync(CancellationToken ct)
    {
        var settings = options.Value;

        // A scope of its own, disposed before the fan-out begins: the DbContext that answers the
        // selection query must not still be alive and shared when the parallel loop starts.
        IReadOnlyList<StaleCharacterRef> due;

        using (var scope = scopeFactory.CreateScope())
        {
            if (!scope.ServiceProvider.GetRequiredService<IBlizzardGateway>().IsConfigured)
            {
                // Missing credentials degrade rather than crash (external.md). Nothing to refresh from.
                logger.LogInformation("Blizzard credentials are not configured; skipping the character refresh pass.");
                return CharacterRefreshResult.Skipped;
            }

            due = await scope.ServiceProvider
                .GetRequiredService<ICharacterRepository>()
                .FindStaleAsync(staleness.StaleBefore(), settings.CharacterBatchSize, ct);
        }

        if (due.Count == 0)
        {
            logger.LogInformation("No characters are past the refresh window.");
            return CharacterRefreshResult.Skipped;
        }

        var refreshed = 0;
        var gone = 0;
        var failed = 0;
        var gate = new Lock();

        // Bounded, never Task.WhenAll over the batch (external.md). "Bounded by the budget" is not the
        // same as bounded concurrency — 200 requests in flight is still 200 leases queued ahead of
        // every user.
        await Parallel.ForEachAsync(
            due,
            new ParallelOptions { MaxDegreeOfParallelism = settings.MaxConcurrentRefreshes, CancellationToken = ct },
            async (candidate, token) =>
            {
                var outcome = await RefreshOneAsync(candidate, token);

                lock (gate)
                {
                    switch (outcome)
                    {
                        case RefreshOutcome.Refreshed: refreshed++; break;
                        case RefreshOutcome.Gone: gone++; break;
                        default: failed++; break;
                    }
                }
            });

        logger.LogInformation(
            "Character refresh pass: {Refreshed} refreshed, {Gone} no longer at Blizzard, {Failed} failed, " +
            "out of {Selected} selected.",
            refreshed,
            gone,
            failed,
            due.Count);

        return new CharacterRefreshResult(due.Count, refreshed, gone, failed, Ran: true);
    }

    private async Task<RefreshOutcome> RefreshOneAsync(StaleCharacterRef candidate, CancellationToken ct)
    {
        // One DI scope per character, and not optional: a DbContext is not thread-safe and this loop
        // runs several iterations at once. Sharing one scope produces the "second operation started on
        // this context" failure under exactly the load that makes it hardest to reproduce.
        using var scope = scopeFactory.CreateScope();

        var gateway = scope.ServiceProvider.GetRequiredService<IBlizzardGateway>();
        var repository = scope.ServiceProvider.GetRequiredService<ICharacterRepository>();

        try
        {
            // Both fetches first, outside the transaction below. ExecuteInTransactionAsync hands the unit
            // to EF's execution strategy, which may run it more than once — an HTTP call inside would
            // fire again on every retry, spending contractual budget and rolled back by nothing.
            var fresh = await gateway.FetchCharacterAsync(candidate.RealmSlug, candidate.Name, ct);

            if (fresh is null)
            {
                // Renamed, transferred or deleted. The row is left alone — removing other people's data
                // on the strength of a 404 is the erasure routine's job. It stays in the selection
                // query: a transient 404 heals itself and a permanent one costs one call a pass.
                return RefreshOutcome.Gone;
            }

            var equipment = await gateway.FetchEquipmentAsync(candidate.RealmSlug, candidate.Name, ct);

            if (await gateway.TryFetchCharacterMediaAsync(candidate.RealmSlug, candidate.Name, ct) is { } media)
            {
                fresh.ApplyMedia(media, fresh.LastSyncedAt);
            }

            await repository.ExecuteInTransactionAsync(
                async token =>
                {
                    var character = await repository.UpsertCharacterAsync(fresh, candidate.RealmId, token);

                    if (equipment is not null)
                    {
                        await repository.ReplaceEquipmentAsync(character.Id, equipment, token);
                    }

                    return character;
                },
                ct);

            return RefreshOutcome.Refreshed;
        }
        catch (BlizzardUnavailableException exception)
        {
            // One character failing does not fail the pass. The row stays stale, so the next pass picks
            // it up again — which is the whole of this job's resumability: the store is the progress.
            logger.LogWarning(
                exception,
                "Could not refresh {Name} on {Region}/{RealmSlug}; it stays due and will be retried.",
                candidate.Name,
                candidate.Region,
                candidate.RealmSlug);

            return RefreshOutcome.Failed;
        }
    }

    private enum RefreshOutcome
    {
        Refreshed,
        Gone,
        Failed,
    }
}

public sealed record CharacterRefreshResult(int Selected, int Refreshed, int Gone, int Failed, bool Ran)
{
    public static readonly CharacterRefreshResult Skipped = new(0, 0, 0, 0, Ran: false);
}
