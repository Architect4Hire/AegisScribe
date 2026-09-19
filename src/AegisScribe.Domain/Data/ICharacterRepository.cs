using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;

namespace AegisScribe.Domain.Data;

public interface ICharacterRepository
{
    // Detail read — the domain entity, gear included, for a character page.
    Task<Character?> FindByRealmAndNameAsync(string region, string realmSlug, string name, CancellationToken ct);

    // Deliberately not a full read: the caller needs to turn "no such character" into a 404 rather than
    // let the foreign key turn it into a 500, and nothing more.
    Task<bool> ExistsAsync(Guid characterId, CancellationToken ct);

    // List read — projects straight to the summary ServiceModel in SQL. Cursor pagination
    // (api-contract.md) is seek-based: afterNameLower/afterId are the decoded tie-break from the
    // previous page, not the opaque cursor string — that encoding stays above this layer.
    Task<IReadOnlyList<CharacterSummaryServiceModel>> SearchAsync(
        string region,
        string? realmSlug,
        string? nameContains,
        string? afterNameLower,
        Guid? afterId,
        int take,
        CancellationToken ct);

    // The sync worker's selection query, oldest first — a compliance queue rather than a work queue, so
    // a backlog becomes lateness on the newest rows rather than the oldest. Equipment counts toward
    // staleness too, or a failed gear fetch leaves a fresh character row whose gear no query picks up.
    Task<IReadOnlyList<StaleCharacterRef>> FindStaleAsync(DateTimeOffset staleBefore, int take, CancellationToken ct);

    // The writes below stage changes only — ExecuteInTransactionAsync calls SaveChangesAsync once for
    // the whole unit — so they commit or roll back together.
    //
    // Keyed on (RealmId, NameLower), the natural key, because Blizzard character ids do not survive a
    // rename or a realm transfer.
    Task<Character> UpsertCharacterAsync(Character fresh, Guid realmId, CancellationToken ct);

    // Reconciles the equipped-item set for a character in place, slot by slot. Icons already known for
    // an incoming item id are copied onto it, since the equipment endpoint carries none.
    Task ReplaceEquipmentAsync(Guid characterId, CharacterEquipment fresh, CancellationToken ct);

    // The media backfill. Characters whose renders were never asked for, or were asked too long ago —
    // oldest first, like FindStaleAsync.
    Task<IReadOnlyList<StaleCharacterRef>> FindMissingMediaAsync(DateTimeOffset staleBefore, int take, CancellationToken ct);

    // Distinct Blizzard item ids whose icon has never been asked for, or was asked too long ago.
    Task<IReadOnlyList<long>> FindItemIdsNeedingIconAsync(DateTimeOffset staleBefore, int take, CancellationToken ct);

    // Unlike the staged writes above, these two execute immediately: each is one self-contained UPDATE
    // with nothing to commit alongside it, run by the worker outside any transaction.
    Task SetMediaAsync(Guid characterId, CharacterMedia media, DateTimeOffset syncedAt, CancellationToken ct);

    Task SetItemIconAsync(long blizzardItemId, string? iconName, DateTimeOffset syncedAt, CancellationToken ct);

    Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken ct);
}
