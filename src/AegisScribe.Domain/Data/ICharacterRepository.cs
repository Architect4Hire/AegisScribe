using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;

namespace AegisScribe.Domain.Data;

public interface ICharacterRepository
{
    // Detail read — the domain entity, gear included, for a character page.
    Task<Character?> FindByRealmAndNameAsync(string region, string realmSlug, string name, CancellationToken ct);

    // Existence by id, for callers holding a character id from elsewhere in the app — claiming (7.2b)
    // and, later, adding to a roster. Deliberately not a full read: the caller needs to turn "no such
    // character" into a 404 rather than let the foreign key turn it into a 500, and nothing more.
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

    // The sync worker's selection query (6.5): characters whose stored data has aged past the refresh
    // policy, oldest first, capped at the run's budget.
    //
    // Oldest first because this is a compliance queue rather than a work queue — the rows closest to
    // breaching the Terms of Use thirty-day window go first, so a backlog turns into lateness on the
    // newest rows instead of the oldest. Equipment counts toward staleness too: it carries its own
    // LastSyncedAt and its own obligation, and a failed gear fetch otherwise leaves a fresh character
    // row whose gear no query would ever pick up again.
    Task<IReadOnlyList<StaleCharacterRef>> FindStaleAsync(DateTimeOffset staleBefore, int take, CancellationToken ct);

    // The write operations below stage changes only — ExecuteInTransactionAsync calls
    // SaveChangesAsync once for the whole unit — so they are composed inside one callback and commit or
    // roll back together.
    // Keyed on (RealmId, NameLower), the natural key, because Blizzard character ids do not survive a
    // rename or a realm transfer. Returns the persisted entity.
    Task<Character> UpsertCharacterAsync(Character fresh, Guid realmId, CancellationToken ct);

    // Reconciles the equipped-item set for a character in place, slot by slot.
    Task ReplaceEquipmentAsync(Guid characterId, CharacterEquipment fresh, CancellationToken ct);

    Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken ct);
}
