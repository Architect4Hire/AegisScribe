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
    /// Paged over mains rather than rows so a main and its alts are never split across a page boundary
    /// — an alt whose main is on the previous page renders as an orphaned ↳ row no client can group.
    /// Never offset: a roster paged by offset duplicates and skips rows while somebody scrolls
    /// (api-contract.md).
    /// </remarks>
    Task<IReadOnlyList<Guid>> ListMainIdsAsync(
        RosterSort sort, string? afterKey, Guid? afterId, int take, CancellationToken ct);

    /// <summary>
    /// Every entry belonging to the given mains — the mains themselves and their alts — projected for
    /// the wire.
    /// </summary>
    /// <param name="includeOfficerNote">
    /// False blanks the note in the projection rather than filtering rows. The caller's rank is
    /// Business's to know, not this layer's.
    /// </param>
    Task<IReadOnlyList<RosterEntryServiceModel>> ListGroupsAsync(
        IReadOnlyList<Guid> mainIds, RosterSort sort, bool includeOfficerNote, CancellationToken ct);

    // Backs the refusal to delete a rank in use. Query-filtered, so another community's holders neither
    // count towards nor protect it.
    Task<int> CountByRankAsync(Guid rankId, CancellationToken ct);

    // Every entry this community holds, mains and alts alike — the first-run checklist only asks
    // whether the roster has anything on it at all.
    Task<int> CountAsync(CancellationToken ct);

    // Null for an id belonging to another community, which is what makes every cross-tenant roster
    // write a 404.
    Task<RosterEntry?> FindEntityAsync(Guid rosterEntryId, CancellationToken ct);

    Task<bool> HasAltsAsync(Guid rosterEntryId, CancellationToken ct);

    // The number the has-alts refusal reports, so an officer knows what they would have orphaned.
    Task<int> CountAltsAsync(Guid rosterEntryId, CancellationToken ct);

    // Query-filtered, so a rank belonging to another community reads as nonexistent rather than being
    // quietly accepted onto a row here.
    Task<bool> RankExistsAsync(Guid rankId, CancellationToken ct);

    Task<bool> IsOnRosterAsync(Guid characterId, CancellationToken ct);

    /// <summary>
    /// Which of <paramref name="characterIds"/> are already on this community's roster.
    /// </summary>
    /// <remarks>
    /// The diff an import subtracts. One query over the candidate set rather than a probe per member:
    /// a roster import is the one roster operation whose input is measured in hundreds.
    /// </remarks>
    Task<IReadOnlyList<Guid>> ListRosteredCharacterIdsAsync(
        IReadOnlyList<Guid> characterIds, CancellationToken ct);

    /// <summary>
    /// Entries whose character belongs to none of the guilds this community currently follows,
    /// newest first, capped at <paramref name="take"/> — plus the full count.
    /// </summary>
    /// <remarks>
    /// What an import reports instead of deleting. Both halves run under the query filter on
    /// RosterEntries AND on TenantGuilds, so "linked" means linked by THIS community: a guild the
    /// community next door follows does not keep a row out of this list.
    /// </remarks>
    Task<(IReadOnlyList<UnaffiliatedRosterEntryServiceModel> Entries, int Total)> ListNotInAnyLinkedGuildAsync(
        int take, CancellationToken ct);

    /// <summary>
    /// Removes this community's roster entries for the given characters, detaching any alts that
    /// pointed at them first.
    /// </summary>
    /// <remarks>
    /// The detach is not tidiness. An officer may link an alt across two members, so a departing
    /// member's entry can be somebody ELSE's main; the alt FK is Restrict, so deleting without
    /// detaching is a 500. Clearing the pointer leaves the other member's character on the roster as a
    /// main — which is the honest outcome, since the relationship it recorded no longer has two ends.
    /// </remarks>
    Task RemoveEntriesForCharactersAsync(IReadOnlyList<Guid> characterIds, CancellationToken ct);

    // Stages the whole import. TenantId is stamped per row by the SaveChanges interceptor, never
    // assigned here (tenancy.md) — which is also what makes a cross-tenant import impossible to write
    // by accident.
    Task AddRangeAsync(IReadOnlyList<RosterEntry> entries, CancellationToken ct);

    // Stages only — every roster write is paired with an audit row inside one transaction. TenantId is
    // stamped by the SaveChanges interceptor, never assigned here (tenancy.md).
    Task AddAsync(RosterEntry entry, CancellationToken ct);

    Task SetRankAsync(Guid rosterEntryId, Guid? tenantRankId, CancellationToken ct);

    Task SetOfficerNoteAsync(Guid rosterEntryId, string? officerNote, CancellationToken ct);

    Task RemoveAsync(RosterEntry entry, CancellationToken ct);

    /// <summary>
    /// Makes an entry an alt of another, but only if that still keeps the model one level deep.
    /// Returns false when it would not.
    /// </summary>
    /// <remarks>
    /// The depth rules live in the statement's WHERE rather than a SELECT before it, and that is
    /// load-bearing: checked separately they are a read-then-write, and two concurrent
    /// opposite-direction links each pass against pre-commit state and then write DIFFERENT rows, so
    /// nothing conflicts and a cycle commits. Same fix as <see cref="SyncBudgetRepository"/>'s
    /// conditional consume.
    /// </remarks>
    Task<bool> TryLinkAltAsync(Guid rosterEntryId, Guid mainRosterEntryId, CancellationToken ct);

    // Clearing a main needs no guard: it cannot create depth or a cycle.
    Task UnlinkAltAsync(Guid rosterEntryId, CancellationToken ct);

    Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken ct);
}
