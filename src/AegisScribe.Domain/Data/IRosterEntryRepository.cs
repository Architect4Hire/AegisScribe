using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;

namespace AegisScribe.Domain.Data;

// Every method runs under RosterEntry's global query filter, so there is no tenantId parameter here
// and an id belonging to another community simply does not resolve (tenancy.md).
public interface IRosterEntryRepository
{
    /// <summary>
    /// The ids of the next page of MAINS, in <paramref name="sort"/> order, keyset-resumed from
    /// (<paramref name="afterKey"/>, <paramref name="afterId"/>).
    /// </summary>
    /// <remarks>
    /// The page is over mains rather than rows so that a main and its alts are never split across a
    /// page boundary — an alt whose main is on the previous page renders as an orphaned ↳ row that no
    /// client can group (7.4). Never offset: a roster sorted by rank and paged by offset duplicates
    /// and skips rows while somebody scrolls, which is exactly mobile infinite scroll
    /// (api-contract.md).
    /// </remarks>
    Task<IReadOnlyList<Guid>> ListMainIdsAsync(
        RosterSort sort, string? afterKey, Guid? afterId, int take, CancellationToken ct);

    /// <summary>
    /// Every entry belonging to the given mains — the mains themselves and their alts — projected for
    /// the wire.
    /// </summary>
    /// <param name="includeOfficerNote">
    /// False blanks the note in the projection rather than filtering rows. Officer-private, and the
    /// caller's rank is Business's to know, not this layer's.
    /// </param>
    Task<IReadOnlyList<RosterEntryServiceModel>> ListGroupsAsync(
        IReadOnlyList<Guid> mainIds, RosterSort sort, bool includeOfficerNote, CancellationToken ct);

    // How many of THIS community's entries hold a rank. Backs the refusal to delete a rank in use
    // (7.2); query-filtered, so another community's holders neither count towards nor protect it.
    Task<int> CountByRankAsync(Guid rankId, CancellationToken ct);

    // The domain entity, for the paths that decide something about the entry. Null for an id belonging
    // to another community, which is what makes every cross-tenant roster write a 404.
    Task<RosterEntry?> FindEntityAsync(Guid rosterEntryId, CancellationToken ct);

    Task<bool> HasAltsAsync(Guid rosterEntryId, CancellationToken ct);

    // How many entries call this one their main — the number the has-alts refusal reports so an
    // officer knows what they would have orphaned.
    Task<int> CountAltsAsync(Guid rosterEntryId, CancellationToken ct);

    // Whether a rank id is one of THIS community's. Query-filtered, so a rank belonging to another
    // community reads as nonexistent rather than being quietly accepted onto a row here.
    Task<bool> RankExistsAsync(Guid rankId, CancellationToken ct);

    Task<bool> IsOnRosterAsync(Guid characterId, CancellationToken ct);

    // TenantId is stamped by the SaveChanges interceptor, never assigned here (tenancy.md). Stages
    // only — every roster write is paired with an audit row inside one transaction.
    Task AddAsync(RosterEntry entry, CancellationToken ct);

    Task SetRankAsync(Guid rosterEntryId, Guid? tenantRankId, CancellationToken ct);

    Task SetOfficerNoteAsync(Guid rosterEntryId, string? officerNote, CancellationToken ct);

    Task RemoveAsync(RosterEntry entry, CancellationToken ct);

    /// <summary>
    /// Makes an entry an alt of another, but only if that still keeps the model one level deep.
    /// Returns false when it would not.
    /// </summary>
    /// <remarks>
    /// The depth rules live in the statement's WHERE rather than in a SELECT before it, and that is
    /// load-bearing: checked separately they are a read-then-write, and two concurrent
    /// opposite-direction links each pass their own check against pre-commit state and then write
    /// DIFFERENT rows, so nothing conflicts and a cycle commits. Same reasoning, and the same fix, as
    /// <see cref="SyncBudgetRepository"/>'s conditional consume.
    /// </remarks>
    Task<bool> TryLinkAltAsync(Guid rosterEntryId, Guid mainRosterEntryId, CancellationToken ct);

    // Clearing a main needs no guard: it cannot create depth or a cycle.
    Task UnlinkAltAsync(Guid rosterEntryId, CancellationToken ct);

    Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken ct);
}
