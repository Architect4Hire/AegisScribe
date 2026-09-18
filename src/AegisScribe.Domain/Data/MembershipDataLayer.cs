using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;

namespace AegisScribe.Domain.Data;

// Five repositories, because removing a member legitimately reaches three tables and the lifecycle
// spans two more. Each of them keeps its own tenant discipline — the two lifecycle repositories take
// tenantId explicitly because their entities have no query filter, while the claim and roster
// repositories run under theirs.
public class MembershipDataLayer(
    ITenantMembershipRepository memberships,
    ITenantInvitationRepository invitations,
    ITenantJoinRequestRepository joinRequests,
    ITenantRepository tenants,
    ICharacterClaimRepository claims,
    IRosterEntryRepository rosterEntries,
    IAuditLogRepository auditLog) : IMembershipDataLayer
{
    // ---- members ----

    public Task<IReadOnlyList<TenantMemberServiceModel>> ListMembersAsync(
        Guid tenantId, DateTimeOffset? afterJoinedAt, string? afterId, int take, CancellationToken ct) =>
        memberships.ListAsync(tenantId, afterJoinedAt, afterId, take, ct);

    public Task<TenantMembership?> FindMembershipAsync(Guid tenantId, string userId, CancellationToken ct) =>
        memberships.FindAsync(tenantId, userId, ct);

    public Task<bool> TrySetRoleAsync(
        Guid tenantId, string userId, TenantRole role, AuditLog auditEntry, CancellationToken ct) =>
        memberships.ExecuteInTransactionAsync(async token =>
        {
            var changed = await memberships.TrySetRoleAsync(tenantId, userId, role, token);

            // Only when the conditional update actually took effect. A row recording a demotion the
            // last-owner rule refused would be worse than no row at all.
            if (changed)
            {
                await auditLog.AddAsync(auditEntry, token);
            }

            return changed;
        }, ct);

    public Task<bool> TryRemoveMemberAsync(
        Guid tenantId, string userId, AuditLog auditEntry, CancellationToken ct) =>
        memberships.ExecuteInTransactionAsync(async token =>
        {
            // The membership goes first, because it carries the rule that can refuse the whole thing.
            // Deleting somebody's claims and roster rows and THEN discovering they were the last owner
            // would leave a community intact but hollowed out.
            var removed = await memberships.TryRemoveAsync(tenantId, userId, token);

            if (!removed)
            {
                return false;
            }

            // What the membership owned in THIS community, and nothing else. Both of these run under
            // their entities' query filters, so neither can reach the same person's rows next door —
            // and no Character row is touched, because their global data is not this community's to
            // delete (auth.md).
            var claimed = await claims.ListCharacterIdsClaimedByAsync(userId, token);

            await rosterEntries.RemoveEntriesForCharactersAsync(claimed, token);
            await claims.RemoveClaimsByUserAsync(userId, token);
            await auditLog.AddAsync(auditEntry, token);

            return true;
        }, ct);

    // ---- invitations ----

    public Task<IReadOnlyList<TenantInvitation>> ListInvitationsAsync(Guid tenantId, int take, CancellationToken ct) =>
        invitations.ListAsync(tenantId, take, ct);

    public Task<TenantInvitation?> FindInvitationAsync(Guid tenantId, Guid invitationId, CancellationToken ct) =>
        invitations.FindAsync(tenantId, invitationId, ct);

    public Task CreateInvitationAsync(TenantInvitation invitation, AuditLog auditEntry, CancellationToken ct) =>
        invitations.ExecuteInTransactionAsync(async token =>
        {
            await invitations.AddAsync(invitation, token);
            await auditLog.AddAsync(auditEntry, token);

            return true;
        }, ct);

    public Task<bool> TryRevokeInvitationAsync(
        Guid tenantId,
        Guid invitationId,
        string revokedByUserId,
        DateTimeOffset revokedAt,
        AuditLog auditEntry,
        CancellationToken ct) =>
        invitations.ExecuteInTransactionAsync(async token =>
        {
            var revoked = await invitations.TryRevokeAsync(
                tenantId, invitationId, revokedByUserId, revokedAt, token);

            if (revoked)
            {
                await auditLog.AddAsync(auditEntry, token);
            }

            return revoked;
        }, ct);

    // ---- accepting an invitation (8.3b) ----

    public Task<TenantInvitation?> FindInvitationByTokenHashAsync(byte[] tokenHash, CancellationToken ct) =>
        invitations.FindByTokenHashAsync(tokenHash, ct);

    public Task<Tenant?> FindTenantByIdAsync(Guid tenantId, CancellationToken ct) =>
        tenants.FindByIdAsync(tenantId, ct);

    // Through the MEMBERSHIP repository's transaction wrapper rather than the invitation one, and
    // deliberately: they share a DbContext so the transaction is the same either way, but only that
    // wrapper translates the (TenantId, UserId) primary key into AlreadyAMemberException. The write
    // that can lose a race here is the membership insert.
    public Task<bool> TryAcceptInvitationAsync(
        byte[] tokenHash,
        TenantMembership membership,
        DateTimeOffset acceptedAt,
        AuditLog auditEntry,
        CancellationToken ct) =>
        memberships.ExecuteInTransactionAsync(async token =>
        {
            // The consume comes first and is the gate. Nothing below happens for a token somebody
            // else just spent.
            if (!await invitations.TryConsumeAsync(tokenHash, membership.UserId, acceptedAt, token))
            {
                return false;
            }

            await memberships.AddAsync(membership, token);

            // They may have been waiting in the queue when the link arrived. Resolving it here is what
            // stops a stale Pending row outliving their joining and tripping the primary key the next
            // time an officer works the queue. Decided BY them, because accepting is what resolved it.
            await joinRequests.TryResolvePendingForUserAsync(
                membership.TenantId,
                membership.UserId,
                JoinRequestStatus.Approved,
                membership.UserId,
                acceptedAt,
                token);

            await auditLog.AddAsync(auditEntry, token);

            return true;
        }, ct);

    public Task RecordAsync(AuditLog auditEntry, CancellationToken ct) =>
        memberships.ExecuteInTransactionAsync(async token =>
        {
            await auditLog.AddAsync(auditEntry, token);

            return true;
        }, ct);

    // ---- join requests ----

    public Task<IReadOnlyList<JoinRequestServiceModel>> ListJoinRequestsAsync(
        Guid tenantId, JoinRequestStatus? status, int take, CancellationToken ct) =>
        joinRequests.ListAsync(tenantId, status, take, ct);

    public Task<TenantJoinRequest?> FindJoinRequestAsync(Guid tenantId, Guid requestId, CancellationToken ct) =>
        joinRequests.FindAsync(tenantId, requestId, ct);

    public Task<bool> HasPendingJoinRequestAsync(Guid tenantId, string userId, CancellationToken ct) =>
        joinRequests.HasPendingAsync(tenantId, userId, ct);

    // No audit row: the lifecycle acts auth.md wants recorded are officers acting on other people, and
    // a person asking to join is neither. The DECISION below is audited.
    public Task CreateJoinRequestAsync(TenantJoinRequest request, CancellationToken ct) =>
        joinRequests.ExecuteInTransactionAsync(async token =>
        {
            await joinRequests.AddAsync(request, token);

            return true;
        }, ct);

    public Task<bool> TryApproveJoinRequestAsync(
        Guid tenantId,
        Guid requestId,
        TenantMembership membership,
        string decidedByUserId,
        DateTimeOffset decidedAt,
        AuditLog auditEntry,
        CancellationToken ct) =>
        joinRequests.ExecuteInTransactionAsync(async token =>
        {
            // The conditional decide comes first and is the gate: it only takes effect on a request
            // still pending, so two officers racing produce exactly one membership.
            var decided = await joinRequests.TryDecideAsync(
                tenantId, requestId, JoinRequestStatus.Approved, decidedByUserId, decidedAt, token);

            if (!decided)
            {
                return false;
            }

            await memberships.AddAsync(membership, token);
            await auditLog.AddAsync(auditEntry, token);

            return true;
        }, ct);

    public Task<bool> TryDeclineJoinRequestAsync(
        Guid tenantId,
        Guid requestId,
        string decidedByUserId,
        DateTimeOffset decidedAt,
        AuditLog auditEntry,
        CancellationToken ct) =>
        joinRequests.ExecuteInTransactionAsync(async token =>
        {
            var decided = await joinRequests.TryDecideAsync(
                tenantId, requestId, JoinRequestStatus.Declined, decidedByUserId, decidedAt, token);

            if (decided)
            {
                await auditLog.AddAsync(auditEntry, token);
            }

            return decided;
        }, ct);

    // ---- the tenant itself ----

    public Task<Tenant?> FindTenantBySlugAsync(string slug, CancellationToken ct) =>
        tenants.FindBySlugAsync(slug, ct);

    public Task<bool> IsMemberAsync(Guid tenantId, string userId, CancellationToken ct) =>
        tenants.IsMemberAsync(tenantId, userId, ct);

    public Task SetJoinPolicyAsync(Guid tenantId, bool acceptsJoinRequests, CancellationToken ct) =>
        tenants.SetJoinPolicyAsync(tenantId, acceptsJoinRequests, ct);
}
