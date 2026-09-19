using AegisScribe.Domain.Data;
using AegisScribe.Domain.Integration.Blizzard;
using Microsoft.Extensions.Options;

namespace AegisScribe.SyncWorker;

// Fills in the images the character and equipment endpoints do not carry:
//
//   character renders — for characters stored before media was synced, or whose renders are aging out.
//                       Every ordinary refresh fetches media too, so this is mostly a one-off backfill.
//   item icons        — the equipment endpoint has none at all. Asked ONCE PER DISTINCT ITEM ID, and the
//                       answer is written to every row wearing that item, so a raid tier piece worn by a
//                       thousand characters costs one call. New snapshots copy known icons on write
//                       (CharacterRepository.ReplaceEquipmentAsync), so only genuinely new items land here.
//
// Global, like every other job in this process: renders and icons are public reference data, and no
// community's sync budget pays for them. Bounded by the shared rate limiter and the batch sizes below.
//
// Resumable by construction: the selection queries ARE the progress. An interrupted pass leaves the
// unfinished rows selectable, and a completed one stamps a SyncedAt that takes them out.
public sealed class MediaBackfillSync(
    IServiceScopeFactory scopeFactory,
    IBlizzardStalenessPolicy staleness,
    IOptions<SyncWorkerOptions> options,
    TimeProvider timeProvider,
    ILogger<MediaBackfillSync> logger)
{
    public async Task<MediaBackfillResult> RunAsync(CancellationToken ct)
    {
        var settings = options.Value;

        IReadOnlyList<StaleCharacterRef> characters;
        IReadOnlyList<long> itemIds;

        // Its own scope, disposed before the fan-out, for the same reason as CharacterRefreshSync: the
        // DbContext answering the selection must not be shared with the parallel loop.
        using (var scope = scopeFactory.CreateScope())
        {
            if (!scope.ServiceProvider.GetRequiredService<IBlizzardGateway>().IsConfigured)
            {
                logger.LogInformation("Blizzard credentials are not configured; skipping the media backfill pass.");
                return MediaBackfillResult.Skipped;
            }

            var repository = scope.ServiceProvider.GetRequiredService<ICharacterRepository>();
            var staleBefore = staleness.StaleBefore();

            characters = await repository.FindMissingMediaAsync(staleBefore, settings.MediaBatchSize, ct);
            itemIds = await repository.FindItemIdsNeedingIconAsync(staleBefore, settings.MediaBatchSize, ct);
        }

        if (characters.Count == 0 && itemIds.Count == 0)
        {
            return MediaBackfillResult.Skipped;
        }

        var parallel = new ParallelOptions { MaxDegreeOfParallelism = settings.MaxConcurrentRefreshes, CancellationToken = ct };
        var renders = 0;
        var icons = 0;
        var failed = 0;
        var gate = new Lock();

        await Parallel.ForEachAsync(characters, parallel, async (candidate, token) =>
        {
            var ok = await FillCharacterMediaAsync(candidate, token);
            lock (gate)
            {
                if (ok) { renders++; } else { failed++; }
            }
        });

        await Parallel.ForEachAsync(itemIds, parallel, async (itemId, token) =>
        {
            var ok = await FillItemIconAsync(itemId, token);
            lock (gate)
            {
                if (ok) { icons++; } else { failed++; }
            }
        });

        logger.LogInformation(
            "Media backfill pass: {Renders} character renders and {Icons} item icons resolved, {Failed} failed.",
            renders,
            icons,
            failed);

        return new MediaBackfillResult(characters.Count, itemIds.Count, renders, icons, failed, Ran: true);
    }

    private async Task<bool> FillCharacterMediaAsync(StaleCharacterRef candidate, CancellationToken ct)
    {
        // One scope per unit: a DbContext is not thread-safe and this loop runs several at once.
        using var scope = scopeFactory.CreateScope();

        var gateway = scope.ServiceProvider.GetRequiredService<IBlizzardGateway>();
        var media = await gateway.TryFetchCharacterMediaAsync(candidate.RealmSlug, candidate.Name, ct);

        if (media is null)
        {
            // Unavailable, not "no renders" — the row stays selectable and the next pass retries it.
            logger.LogWarning(
                "Could not fetch renders for {Name} on {Region}/{RealmSlug}; will retry.",
                candidate.Name,
                candidate.Region,
                candidate.RealmSlug);

            return false;
        }

        await scope.ServiceProvider
            .GetRequiredService<ICharacterRepository>()
            .SetMediaAsync(candidate.Id, media, timeProvider.GetUtcNow(), ct);

        return true;
    }

    private async Task<bool> FillItemIconAsync(long itemId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();

        var gateway = scope.ServiceProvider.GetRequiredService<IBlizzardGateway>();

        ItemIconLookup lookup;

        try
        {
            lookup = await gateway.FetchItemIconAsync(itemId, ct) ?? ItemIconLookup.NotFound;
        }
        catch (BlizzardUnavailableException exception)
        {
            logger.LogWarning(exception, "Could not fetch the icon for item {ItemId}; will retry.", itemId);
            return false;
        }

        // A 404 is recorded too, as a null name with a timestamp: "asked, and there is none". Without the
        // stamp, an item with no media would cost a call every pass forever.
        await scope.ServiceProvider
            .GetRequiredService<ICharacterRepository>()
            .SetItemIconAsync(itemId, lookup.IconName, timeProvider.GetUtcNow(), ct);

        return true;
    }
}

public sealed record MediaBackfillResult(
    int CharactersSelected,
    int ItemsSelected,
    int RendersResolved,
    int IconsResolved,
    int Failed,
    bool Ran)
{
    public static readonly MediaBackfillResult Skipped = new(0, 0, 0, 0, 0, Ran: false);
}
