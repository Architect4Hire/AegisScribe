using AegisScribe.Domain.Integration.Blizzard;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using Microsoft.Extensions.Logging;

namespace AegisScribe.Domain.Data;

// The seam the add-endpoint skill's step 6 describes: local first, Blizzard only when the stored row is
// missing or stale, persist what comes back, and fall back to the stale row when Blizzard cannot answer.
//
// Business is unchanged by all of it — it asks for a character and gets one. That is the point of
// putting the sequencing here.
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
            return CharacterReadResult.Current(local);
        }

        return await FetchAndPersistAsync(region, realmSlug, name, local, ct);
    }

    public async Task<CharacterReadResult> RefreshCharacterAsync(
        string region,
        string realmSlug,
        string name,
        CancellationToken ct)
    {
        // Identical to the read above from here down — the only difference is that the staleness check
        // never happens. Sharing the body rather than copying it is the point: a forced refresh that
        // persisted differently from a lazy one would be a second definition of "refreshed", and the
        // one that drifted would be whichever is exercised less.
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
        // Checked before anything else is attempted so that an offline development machine costs
        // nothing per read: no token mint, no request, no resilience pipeline. The app runs on seeded
        // data, which external.md calls a first-class case rather than a fallback.
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
            // Blizzard 404'd: the realm or the character does not exist there. If we hold a row anyway
            // it is one Blizzard no longer knows about — renamed, transferred or deleted — so it is
            // kept and flagged rather than removed. Deleting other people's data on the strength of a
            // 404 is not a call a read path gets to make; the erasure routine is the sanctioned path.
            return local is null ? CharacterReadResult.NotFound : CharacterReadResult.Stale(local);
        }

        await PersistAsync(fetched, ct);

        // Re-read rather than returning the entity the upsert handed back. The stored shape is what the
        // caller expects — realm included, equipment included, untracked — and composing that by hand
        // from three staged writes is a second definition of the read that would drift from this one.
        var persisted = await repository.FindByRealmAndNameAsync(region, realmSlug, name, ct);

        return persisted is null ? CharacterReadResult.NotFound : CharacterReadResult.Current(persisted);
    }

    public Task<IReadOnlyList<CharacterSummaryServiceModel>> SearchAsync(
        string region, string? realmSlug, string? nameContains, string? afterNameLower, Guid? afterId, int take, CancellationToken ct) =>
        repository.SearchAsync(region, realmSlug, nameContains, afterNameLower, afterId, take, ct);

    public Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken ct) =>
        repository.ExecuteInTransactionAsync(operation, ct);

    // Equipment counts toward staleness even though it is fetched from its own endpoint with its own
    // LastSyncedAt: a read refreshes both together, so a character row whose gear is missing or older
    // than the policy is not actually current no matter what its own column says.
    private bool IsStale(Character character) =>
        staleness.IsStale(character.LastSyncedAt)
        || character.Equipment is null
        || staleness.IsStale(character.Equipment.LastSyncedAt);

    // Every network call this read makes, all of them before the transaction below opens. Nothing here
    // touches the repository.
    private async Task<BlizzardFetch> FetchAsync(string region, string realmSlug, string name, CancellationToken ct)
    {
        // The realm first, and only when we do not already hold it. Character.RealmId is not nullable,
        // so an unknown realm has to be resolved before a character on it can be stored at all — and
        // resolving it costs a call, which is why a realm we already have short-circuits this.
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

        return new BlizzardFetch(character, equipment, realm?.Id, fetchedRealm);
    }

    // The write half, and the reason the fetch above is a separate method. ExecuteInTransactionAsync
    // hands the unit to EF's execution strategy, which MAY RUN IT MORE THAN ONCE — Aspire's SQL Server
    // integration enables retry-on-failure. An HTTP call inside this callback would therefore fire again
    // on every retry, spending rate-limit budget the Terms of Use treat as contractual, and would be
    // rolled back by nothing. Fetch first, then transact.
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
    // had nothing to give — a 404 on the realm or on the character — and there is nothing to persist.
    //
    // Exactly one of ExistingRealmId and FetchedRealm is set when Character is not null. The gateway
    // leaves Character.RealmId unset by design (it has no store and will not fabricate a realm), so the
    // id has to be carried here rather than read back off the entity.
    private sealed record BlizzardFetch(
        Character? Character,
        CharacterEquipment? Equipment,
        Guid? ExistingRealmId,
        Realm? FetchedRealm)
    {
        public static readonly BlizzardFetch Nothing = new(null, null, null, null);
    }
}
