using AegisScribe.Domain.Integration.Blizzard;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using Microsoft.Extensions.Logging;

namespace AegisScribe.Domain.Data;

// Cache-first: local, then Blizzard only when the stored row is missing or stale, persist what comes
// back, and fall back to the stale row when Blizzard cannot answer. Business is unchanged by all of it
// — it asks for a character and gets one, which is the point of putting the sequencing here.
public class CharacterDataLayer(
    ICharacterRepository repository,
    IRealmRepository realms,
    IBlizzardGateway gateway,
    IBlizzardStalenessPolicy staleness,
    ILogger<CharacterDataLayer> logger) : ICharacterDataLayer
{
    public async Task<CharacterReadResult> GetCharacterAsync(
        string region,
        string realmSlug,
        string name,
        CancellationToken ct)
    {
        var local = await repository.FindByRealmAndNameAsync(region, realmSlug, name, ct);

        if (local is not null && !IsStale(local))
        {
            await FillNeverAskedMediaAsync(local, realmSlug, name, ct);
            await ResolveUnknownIconsAsync(local.Equipment?.EquippedItems ?? [], local.LastSyncedAt, ct);

            return CharacterReadResult.Current(local);
        }

        return await FetchAndPersistAsync(region, realmSlug, name, local, ct);
    }

    // Icons for items nobody has asked Blizzard about yet — typically a guild member opened for the
    // first time, whose gear this very request just fetched. Somebody is looking at the page now, so
    // waiting for the worker's next pass would show them a rail of outlines that fills in only on a
    // reload. Bounded by construction: one character's equipment is at most sixteen slots, and only
    // items unknown to the whole store reach Blizzard — the worker still covers anything that fails here.
    //
    // Fetches concurrently (bounded by the slot count, and every call still takes a lease from the
    // shared limiter), writes sequentially: the repository's DbContext is not thread-safe. Stamped with
    // the character's fetch instant, like the renders, so nothing looks fresher than its row.
    private async Task ResolveUnknownIconsAsync(IEnumerable<EquippedItem> items, DateTimeOffset syncedAt, CancellationToken ct)
    {
        var unknown = items
            .Where(item => item.IconSyncedAt is null && item.IconName is null && item.BlizzardItemId > 0)
            .ToList();

        if (unknown.Count == 0 || !gateway.IsConfigured)
        {
            return;
        }

        var lookups = await Task.WhenAll(
            unknown.Select(item => item.BlizzardItemId).Distinct().Select(async itemId =>
            {
                try
                {
                    return (ItemId: itemId, Lookup: await gateway.FetchItemIconAsync(itemId, ct) ?? ItemIconLookup.NotFound);
                }
                catch (BlizzardUnavailableException)
                {
                    // Left unresolved; the worker's backfill picks it up.
                    return (ItemId: itemId, Lookup: (ItemIconLookup?)null);
                }
            }));

        foreach (var (itemId, lookup) in lookups)
        {
            if (lookup is null)
            {
                continue;
            }

            await repository.SetItemIconAsync(itemId, lookup.IconName, syncedAt, ct);

            // The read path's own copy too, for the fresh-row case that returns without re-reading.
            foreach (var item in unknown.Where(item => item.BlizzardItemId == itemId))
            {
                item.IconName = lookup.IconName;
                item.IconSyncedAt = syncedAt;
            }
        }
    }

    // A current row whose renders nobody has asked Blizzard for — stored before media was synced, or
    // arrived through a guild roster, which carries none. Somebody is looking at it right now, so one
    // call fetches the renders rather than leaving the page on its fallbacks until the worker's backfill
    // reaches it. Only the media: the rest of the row is current and re-fetching it would spend two
    // calls to learn nothing.
    //
    // Stamped with the row's own LastSyncedAt rather than now, so the renders never look fresher than
    // the character they belong to and are re-asked when it is.
    private async Task FillNeverAskedMediaAsync(Character local, string realmSlug, string name, CancellationToken ct)
    {
        if (local.MediaSyncedAt is not null || !gateway.IsConfigured)
        {
            return;
        }

        if (await gateway.TryFetchCharacterMediaAsync(realmSlug, name, ct) is not { } media)
        {
            // Unavailable: serve the row without renders; the next view or the backfill tries again.
            return;
        }

        await repository.SetMediaAsync(local.Id, media, local.LastSyncedAt, ct);
        local.ApplyMedia(media, local.LastSyncedAt);
    }

    public async Task<CharacterReadResult> RefreshCharacterAsync(
        string region,
        string realmSlug,
        string name,
        CancellationToken ct)
    {
        // Identical to the read above except that the staleness check never happens. Sharing the body
        // rather than copying it is the point: a forced refresh that persisted differently from a lazy
        // one would be a second definition of "refreshed", and the drifting one would be whichever is
        // exercised less.
        var local = await repository.FindByRealmAndNameAsync(region, realmSlug, name, ct);

        return await FetchAndPersistAsync(region, realmSlug, name, local, ct);
    }

    private async Task<CharacterReadResult> FetchAndPersistAsync(
        string region,
        string realmSlug,
        string name,
        Character? local,
        CancellationToken ct)
    {
        // Before anything else is attempted, so an offline development machine costs nothing per read.
        // The app runs on seeded data, which external.md calls a first-class case rather than a
        // fallback.
        if (!gateway.IsConfigured)
        {
            return local is null ? CharacterReadResult.NotFound : CharacterReadResult.Stale(local);
        }

        BlizzardFetch fetched;

        try
        {
            fetched = await FetchAsync(region, realmSlug, name, ct);
        }
        catch (BlizzardUnavailableException exception)
        {
            // Unreachable, throttled, or answering with something we cannot map. A stale row beats no
            // row, and the caller is told which it got.
            logger.LogWarning(
                exception,
                "Could not refresh {Name} on {Region}/{RealmSlug} from Blizzard; serving stored data.",
                name,
                region,
                realmSlug);

            return CharacterReadResult.Stale(local);
        }

        if (fetched.Character is null)
        {
            // Blizzard 404'd. A row we hold anyway is one Blizzard no longer knows about — renamed,
            // transferred or deleted — so it is kept and flagged rather than removed. Deleting other
            // people's data on the strength of a 404 is not a call a read path gets to make; the
            // erasure routine is the sanctioned path.
            return local is null ? CharacterReadResult.NotFound : CharacterReadResult.Stale(local);
        }

        await PersistAsync(fetched, ct);

        // After the write, so the icons land on persisted rows, and before the re-read below, so the
        // page that asked gets them. The equipment write already copied every icon known for these
        // item ids; what is left here is only what nobody has ever worn.
        if (fetched.Equipment is not null)
        {
            await ResolveUnknownIconsAsync(fetched.Equipment.EquippedItems, fetched.Character.LastSyncedAt, ct);
        }

        // Re-read rather than returning the entity the upsert handed back. The stored shape is what the
        // caller expects — realm included, equipment included, untracked — and composing that by hand
        // from three staged writes would be a second definition of the read.
        var persisted = await repository.FindByRealmAndNameAsync(region, realmSlug, name, ct);

        return persisted is null ? CharacterReadResult.NotFound : CharacterReadResult.Current(persisted);
    }

    public Task<IReadOnlyList<CharacterSummaryServiceModel>> SearchAsync(
        string region, string? realmSlug, string? nameContains, string? afterNameLower, Guid? afterId, int take, CancellationToken ct) =>
        repository.SearchAsync(region, realmSlug, nameContains, afterNameLower, afterId, take, ct);

    public Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken ct) =>
        repository.ExecuteInTransactionAsync(operation, ct);

    // Equipment counts toward staleness even though it has its own LastSyncedAt: a read refreshes both
    // together, so a character whose gear is missing or older than the policy is not current no matter
    // what its own column says.
    private bool IsStale(Character character) =>
        staleness.IsStale(character.LastSyncedAt)
        || character.Equipment is null
        || staleness.IsStale(character.Equipment.LastSyncedAt);

    // Every network call this read makes, all before the transaction below opens. Nothing here touches
    // the repository.
    private async Task<BlizzardFetch> FetchAsync(string region, string realmSlug, string name, CancellationToken ct)
    {
        // The realm first, and only when we do not already hold it. Character.RealmId is not nullable,
        // so an unknown realm has to be resolved before a character on it can be stored — and resolving
        // it costs a call, which is why a realm we already have short-circuits this.
        var realm = await realms.FindAsync(region, realmSlug, ct);

        var fetchedRealm = realm is null
            ? await gateway.FetchRealmAsync(region, realmSlug, ct)
            : null;

        if (realm is null && fetchedRealm is null)
        {
            // No such realm, here or at Blizzard. There is nothing to hang a character from, and asking
            // for one would spend a call on a lookup that cannot be persisted.
            return BlizzardFetch.Nothing;
        }

        var character = await gateway.FetchCharacterAsync(realmSlug, name, ct);

        if (character is null)
        {
            return BlizzardFetch.Nothing;
        }

        var equipment = await gateway.FetchEquipmentAsync(realmSlug, name, ct);

        // Stamped with the summary's own fetch instant, so media and character age together. Left unset
        // when Blizzard could not be asked, and the upsert then keeps the renders already stored.
        if (await gateway.TryFetchCharacterMediaAsync(realmSlug, name, ct) is { } media)
        {
            character.ApplyMedia(media, character.LastSyncedAt);
        }

        return new BlizzardFetch(character, equipment, realm?.Id, fetchedRealm);
    }

    // The write half, and the reason the fetch above is a separate method. ExecuteInTransactionAsync
    // hands the unit to EF's execution strategy, which MAY RUN IT MORE THAN ONCE. An HTTP call inside
    // this callback would fire again on every retry, spending rate-limit budget the Terms of Use treat
    // as contractual, and would be rolled back by nothing. Fetch first, then transact.
    private Task PersistAsync(BlizzardFetch fetched, CancellationToken ct) =>
        repository.ExecuteInTransactionAsync(
            async token =>
            {
                // A realm we already held contributes its id and no write; one we just fetched is
                // upserted first, because the character row cannot reference a realm that is not there.
                var realmId = fetched.FetchedRealm is null
                    ? fetched.ExistingRealmId!.Value
                    : (await realms.UpsertAsync(fetched.FetchedRealm, token)).Id;

                var character = await repository.UpsertCharacterAsync(fetched.Character!, realmId, token);

                if (fetched.Equipment is not null)
                {
                    await repository.ReplaceEquipmentAsync(character.Id, fetched.Equipment, token);
                }

                return character;
            },
            ct);

    // Everything one refresh brought back, staged for the transaction. A null Character means Blizzard
    // had nothing to give and there is nothing to persist.
    //
    // Exactly one of ExistingRealmId and FetchedRealm is set when Character is not null: the gateway
    // leaves Character.RealmId unset by design, so the id is carried here rather than read off the
    // entity.
    private sealed record BlizzardFetch(
        Character? Character,
        CharacterEquipment? Equipment,
        Guid? ExistingRealmId,
        Realm? FetchedRealm)
    {
        public static readonly BlizzardFetch Nothing = new(null, null, null, null);
    }
}
