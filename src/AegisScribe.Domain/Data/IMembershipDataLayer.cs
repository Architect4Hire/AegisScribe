using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;

namespace AegisScribe.Domain.Data;

// Where the membership lifecycle's data operations are composed. Every tenantId below is
// ITenantContext.TenantId — or, for the one tenant-less path, the id a slug resolved to — and never a
// value the client supplied.
//
// Two operations here are genuinely composite; the rest are pass-throughs kept as the seam Business
// depends on:
//   - ApproveJoinRequestAsync decides the request AND creates the membership in one transaction;
//   - RemoveMemberAsync detaches, deletes across three tables and audits in one transaction.
public interface IMembershipDataLayer
{
    // ---- members ----

    Task<IReadOnlyList<TenantMemberServiceModel>> ListMembersAsync(
        Guid tenantId, DateTimeOffset? afterJoinedAt, string? afterId, int take, CancellationToken ct);

    Task<TenantMembership?> FindMembershipAsync(Guid tenantId, string userId, CancellationToken ct);

    // False when the last-owner rule refused it — the guard is inside the UPDATE, not in front of it.
    Task<bool> TrySetRoleAsync(
        Guid tenantId, string userId, TenantRole role, AuditLog auditEntry, CancellationToken ct);

    /// <summary>
    /// Removes a member and everything that membership owned in this community. False when the
    /// last-owner rule refused it.
    /// </summary>
    /// <remarks>
    /// auth.md: "the member's RosterEntry rows in that tenant go with it. Their global character data
    /// does not." So this deletes the membership, their claims here and the roster entries for the
    /// characters they claimed here — and touches no Character row.
    /// </remarks>
    Task<bool> TryRemoveMemberAsync(Guid tenantId, string userId, AuditLog auditEntry, CancellationToken ct);

    // ---- invitations ----

    Task<IReadOnlyList<TenantInvitation>> ListInvitationsAsync(Guid tenantId, int take, CancellationToken ct);

    Task<TenantInvitation?> FindInvitationAsync(Guid tenantId, Guid invitationId, CancellationToken ct);

    Task CreateInvitationAsync(TenantInvitation invitation, AuditLog auditEntry, CancellationToken ct);

    // False when it was already accepted or revoked.
    Task<bool> TryRevokeInvitationAsync(
        Guid tenantId,
        Guid invitationId,
        string revokedByUserId,
        DateTimeOffset revokedAt,
        AuditLog auditEntry,
        CancellationToken ct);

    // ---- accepting an invitation (8.3b) ----

    // No tenantId, and that is the point: the accepter is not a member of anything yet, so the token
    // is the only thing naming a community. Null for a hash nothing matches.
    Task<TenantInvitation?> FindInvitationByTokenHashAsync(byte[] tokenHash, CancellationToken ct);

    Task<Tenant?> FindTenantByIdAsync(Guid tenantId, CancellationToken ct);

    /// <summary>
    /// Spends the invitation, creates the membership, resolves any pending request that person had
    /// here, and audits it — or does none of those. False when the token was no longer live.
    /// </summary>
    /// <remarks>
    /// One transaction, and the boundary is load-bearing in both directions: a membership that
    /// committed without spending the token would let one link admit a crowd, and a token spent
    /// without a membership would strand somebody outside a community holding a dead link. A primary
    /// key violation on the membership therefore rolls the consume back too, leaving the token live.
    /// </remarks>
    Task<bool> TryAcceptInvitationAsync(
        byte[] tokenHash,
        TenantMembership membership,
        DateTimeOffset acceptedAt,
        AuditLog auditEntry,
        CancellationToken ct);

    // A refusal worth recording: somebody presented a token belonging to THIS community that was
    // expired, spent or revoked. Its own transaction, since nothing else happened.
    Task RecordAsync(AuditLog auditEntry, CancellationToken ct);

    // ---- join requests ----

    Task<IReadOnlyList<JoinRequestServiceModel>> ListJoinRequestsAsync(
        Guid tenantId, JoinRequestStatus? status, int take, CancellationToken ct);

    Task<TenantJoinRequest?> FindJoinRequestAsync(Guid tenantId, Guid requestId, CancellationToken ct);

    Task<bool> HasPendingJoinRequestAsync(Guid tenantId, string userId, CancellationToken ct);

    Task CreateJoinRequestAsync(TenantJoinRequest request, CancellationToken ct);

    // Decides the request and creates the membership together — or neither. An approval that committed
    // without its membership is a request nobody can approve twice and a person who never joined.
    Task<bool> TryApproveJoinRequestAsync(
        Guid tenantId,
        Guid requestId,
        TenantMembership membership,
        string decidedByUserId,
        DateTimeOffset decidedAt,
        AuditLog auditEntry,
        CancellationToken ct);

    Task<bool> TryDeclineJoinRequestAsync(
        Guid tenantId,
        Guid requestId,
        string decidedByUserId,
        DateTimeOffset decidedAt,
        AuditLog auditEntry,
        CancellationToken ct);

    // ---- the tenant itself ----

    Task<Tenant?> FindTenantBySlugAsync(string slug, CancellationToken ct);

    Task<bool> IsMemberAsync(Guid tenantId, string userId, CancellationToken ct);

    Task SetJoinPolicyAsync(Guid tenantId, bool acceptsJoinRequests, CancellationToken ct);
}
