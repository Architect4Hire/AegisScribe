using AegisScribe.Domain.Managers.Models.Domain;

namespace AegisScribe.Domain.Data;

// TenantInvitation is not ITenantScoped (see the entity), so every method here names its tenant —
// always ITenantContext.TenantId, never a value from the client.
//
// The two exceptions are deliberate and are the whole point of the design: FindByTokenHashAsync and
// TryConsumeAsync take NO tenant, because acceptance happens before the accepter is a member of
// anything. The token is the only thing naming a community, and the tenant it resolves to is whatever
// the row it finds says.
public interface ITenantInvitationRepository
{
    /// <summary>The invitation a token hash names, in whichever community minted it. Null for none.</summary>
    /// <remarks>
    /// The only tenant-less read on this interface, and the hash's unique index is what makes it
    /// sound: one token can never name two communities. Lookup is by HASH rather than by the token,
    /// so a stolen database row still cannot be replayed as an invitation.
    /// </remarks>
    Task<TenantInvitation?> FindByTokenHashAsync(byte[] tokenHash, CancellationToken ct);

    /// <summary>
    /// Spends an invitation, but only if it is live. Returns false when it was already accepted,
    /// revoked, or had expired.
    /// </summary>
    /// <remarks>
    /// This statement IS the single-use guarantee. Every condition lives in its WHERE rather than in a
    /// SELECT before it: read-then-write here means two people clicking the same forwarded link both
    /// see it live, both write, and one invitation admits two people. Expiry is compared against the
    /// caller's clock inside the same statement for the same reason.
    ///
    /// It is not the only guard. The (TenantId, UserId) primary key on TenantMembership still refuses
    /// a second membership even if this one were wrong — defence in depth, which is why the consume
    /// and the insert share a transaction: a refused insert rolls the consume back and the token
    /// stays live rather than being burned on nothing.
    /// </remarks>
    Task<bool> TryConsumeAsync(
        byte[] tokenHash, string acceptedByUserId, DateTimeOffset acceptedAt, CancellationToken ct);

    // Outstanding first, then the decided ones, newest first. Small by nature — a community does not
    // accumulate thousands of live invitations — so no cursor.
    Task<IReadOnlyList<TenantInvitation>> ListAsync(Guid tenantId, int take, CancellationToken ct);

    Task<TenantInvitation?> FindAsync(Guid tenantId, Guid invitationId, CancellationToken ct);

    /// <summary>
    /// Marks an invitation revoked, but only if it is still outstanding. Returns false when it was
    /// already accepted or revoked.
    /// </summary>
    /// <remarks>
    /// Guard in the WHERE: an accept (8.3b) and a revoke can race, and whichever statement takes the
    /// row lock first wins cleanly. A read-then-write here would let an officer revoke an invitation
    /// that had already been accepted, leaving a member whose invitation says it was never used.
    /// </remarks>
    Task<bool> TryRevokeAsync(
        Guid tenantId, Guid invitationId, string revokedByUserId, DateTimeOffset revokedAt, CancellationToken ct);

    // Stages only — a new invitation shares its transaction with the audit row.
    Task AddAsync(TenantInvitation invitation, CancellationToken ct);

    Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken ct);
}
