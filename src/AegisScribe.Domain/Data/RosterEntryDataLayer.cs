using AegisScribe.Domain.Managers.Models.Domain;

namespace AegisScribe.Domain.Data;

// Where the roster's data operations are composed. Two are genuinely composite; the rest are
// pass-throughs kept as the seam Business depends on:
//
//   - ListAsync is a two-query page — the mains for this cursor position, then everything in their
//     groups — so a group is never split across a page boundary.
//   - every write pairs with its audit row in a single transaction.
//
// The claim and membership lookups reach other repositories rather than other tables, so every query
// keeps its own tenant filter.
public class RosterEntryDataLayer(
    IRosterEntryRepository repository,
    ICharacterClaimRepository claims,
    ICharacterRepository characters,
    ITenantRepository tenants,
    IAuditLogRepository auditLog) : IRosterEntryDataLayer
{
    public async Task<RosterPage> ListAsync(
        RosterSort sort, string? afterKey, Guid? afterId, int take, bool includeOfficerNote, CancellationToken ct)
    {
        var mainIds = await repository.ListMainIdsAsync(sort, afterKey, afterId, take, ct);
        var items = await repository.ListGroupsAsync(mainIds, sort, includeOfficerNote, ct);

        return new RosterPage(items, mainIds.Count);
    }

    public Task<RosterEntry?> FindEntityAsync(Guid rosterEntryId, CancellationToken ct) =>
        repository.FindEntityAsync(rosterEntryId, ct);

    public Task<bool> HasAltsAsync(Guid rosterEntryId, CancellationToken ct) =>
        repository.HasAltsAsync(rosterEntryId, ct);

    public Task<int> CountAltsAsync(Guid rosterEntryId, CancellationToken ct) =>
        repository.CountAltsAsync(rosterEntryId, ct);

    public Task<bool> RankExistsAsync(Guid rankId, CancellationToken ct) =>
        repository.RankExistsAsync(rankId, ct);

    public Task<bool> IsOnRosterAsync(Guid characterId, CancellationToken ct) =>
        repository.IsOnRosterAsync(characterId, ct);

    public Task<bool> CharacterExistsAsync(Guid characterId, CancellationToken ct) =>
        characters.ExistsAsync(characterId, ct);

    public async Task<bool> IsClaimedByAsync(Guid characterId, string userId, CancellationToken ct) =>
        await FindClaimantAsync(characterId, ct) == userId;

    public async Task<string?> FindClaimantAsync(Guid characterId, CancellationToken ct) =>
        (await claims.FindByCharacterAsync(characterId, ct))?.ClaimedByUserId;

    public Task<TenantRole?> GetTenantRoleAsync(Guid tenantId, string userId, CancellationToken ct) =>
        tenants.GetRoleAsync(tenantId, userId, ct);

    public Task AddAsync(RosterEntry entry, AuditLog? auditEntry, CancellationToken ct) =>
        WriteAsync(token => repository.AddAsync(entry, token), auditEntry, ct);

    public Task SetRankAsync(Guid rosterEntryId, Guid? tenantRankId, AuditLog? auditEntry, CancellationToken ct) =>
        WriteAsync(token => repository.SetRankAsync(rosterEntryId, tenantRankId, token), auditEntry, ct);

    public Task SetOfficerNoteAsync(
        Guid rosterEntryId, string? officerNote, AuditLog? auditEntry, CancellationToken ct) =>
        WriteAsync(token => repository.SetOfficerNoteAsync(rosterEntryId, officerNote, token), auditEntry, ct);

    public Task RemoveAsync(RosterEntry entry, AuditLog? auditEntry, CancellationToken ct) =>
        WriteAsync(token => repository.RemoveAsync(entry, token), auditEntry, ct);

    public Task<bool> TryLinkAltAsync(
        Guid rosterEntryId, Guid mainRosterEntryId, AuditLog? auditEntry, CancellationToken ct) =>
        repository.ExecuteInTransactionAsync(async token =>
        {
            var linked = await repository.TryLinkAltAsync(rosterEntryId, mainRosterEntryId, token);

            // Only when the conditional update actually took effect: a row recording a link that lost
            // its race would be worse than no row at all.
            if (linked && auditEntry is not null)
            {
                await auditLog.AddAsync(auditEntry, token);
            }

            return linked;
        }, ct);

    public Task UnlinkAltAsync(Guid rosterEntryId, AuditLog? auditEntry, CancellationToken ct) =>
        WriteAsync(token => repository.UnlinkAltAsync(rosterEntryId, token), auditEntry, ct);

    // One transaction covering the change and the record of it. An officer acting on somebody else's
    // standing and the evidence of them having done it are one act — a write that committed without
    // its audit row is precisely the untraceable officer action the audit table exists to prevent.
    //
    // Nothing but repository calls inside the callback; it is retryable.
    private Task WriteAsync(Func<CancellationToken, Task> write, AuditLog? auditEntry, CancellationToken ct) =>
        repository.ExecuteInTransactionAsync(async token =>
        {
            await write(token);

            if (auditEntry is not null)
            {
                await auditLog.AddAsync(auditEntry, token);
            }

            return true;
        }, ct);
}
