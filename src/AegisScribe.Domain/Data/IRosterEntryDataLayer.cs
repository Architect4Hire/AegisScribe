using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;

namespace AegisScribe.Domain.Data;

public interface IRosterEntryDataLayer
{
    // The whole page: the mains for this cursor position, plus their alts, arranged. Returns the main
    // COUNT alongside, because "is there another page" is a question about mains and the row count
    // cannot answer it.
    Task<RosterPage> ListAsync(
        RosterSort sort, string? afterKey, Guid? afterId, int take, bool includeOfficerNote, CancellationToken ct);

    Task<RosterEntry?> FindEntityAsync(Guid rosterEntryId, CancellationToken ct);

    Task<bool> HasAltsAsync(Guid rosterEntryId, CancellationToken ct);

    Task<int> CountAltsAsync(Guid rosterEntryId, CancellationToken ct);

    Task<bool> RankExistsAsync(Guid rankId, CancellationToken ct);

    Task<bool> IsOnRosterAsync(Guid characterId, CancellationToken ct);

    Task<bool> CharacterExistsAsync(Guid characterId, CancellationToken ct);

    // Whether the caller holds the claim on the character behind a roster entry. The input to alt
    // linking's resource rule, and to whether any roster write is somebody reaching into someone
    // else's data.
    Task<bool> IsClaimedByAsync(Guid characterId, string userId, CancellationToken ct);

    Task<string?> FindClaimantAsync(Guid characterId, CancellationToken ct);

    // The caller's membership role in the resolved tenant. Takes both values explicitly because
    // TenantMembership is deliberately NOT ITenantScoped (tenancy.md), so there is no ambient filter
    // to lean on and every query against it names its tenant.
    Task<TenantRole?> GetTenantRoleAsync(Guid tenantId, string userId, CancellationToken ct);

    // Every write below pairs with its audit row inside one transaction — together or not at all. A
    // null auditEntry means the actor was managing their own characters, which is nobody's business
    // but theirs.
    Task AddAsync(RosterEntry entry, AuditLog? auditEntry, CancellationToken ct);

    Task SetRankAsync(Guid rosterEntryId, Guid? tenantRankId, AuditLog? auditEntry, CancellationToken ct);

    Task SetOfficerNoteAsync(Guid rosterEntryId, string? officerNote, AuditLog? auditEntry, CancellationToken ct);

    Task RemoveAsync(RosterEntry entry, AuditLog? auditEntry, CancellationToken ct);

    // Links, and writes the audit row alongside it — together or not at all, and ONLY when the link
    // actually took effect. False means the depth rules refused it at write time, which under
    // concurrency is the only place they can be enforced.
    Task<bool> TryLinkAltAsync(Guid rosterEntryId, Guid mainRosterEntryId, AuditLog? auditEntry, CancellationToken ct);

    Task UnlinkAltAsync(Guid rosterEntryId, AuditLog? auditEntry, CancellationToken ct);
}

// The rows of a page and how many MAINS produced them. HasMore is decided on the main count, since
// that is the unit the page is taken in (7.4).
public sealed record RosterPage(IReadOnlyList<RosterEntryServiceModel> Items, int MainCount);
