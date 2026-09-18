using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;

namespace AegisScribe.Domain.Data;

// TenantJoinRequest is not ITenantScoped (see the entity), so every method names its tenant. For the
// officer-facing methods that is ITenantContext.TenantId; for AddAsync it is the id the SLUG resolved
// to, since the requester has no membership and therefore no resolved tenant. Neither is ever a value
// the client supplied directly.
public interface ITenantJoinRequestRepository
{
    Task<IReadOnlyList<JoinRequestServiceModel>> ListAsync(
        Guid tenantId, JoinRequestStatus? status, int take, CancellationToken ct);

    Task<TenantJoinRequest?> FindAsync(Guid tenantId, Guid requestId, CancellationToken ct);

    Task<bool> HasPendingAsync(Guid tenantId, string userId, CancellationToken ct);

    /// <summary>
    /// Records a decision, but only on a request that is still pending. Returns false when it had
    /// already been decided.
    /// </summary>
    /// <remarks>
    /// Guard in the WHERE. Two officers opening the same queue and clicking approve and decline is an
    /// ordinary Tuesday; without this both read Pending, both write, and the row ends up saying
    /// whichever committed last while the membership from the approve exists regardless.
    /// </remarks>
    Task<bool> TryDecideAsync(
        Guid tenantId,
        Guid requestId,
        JoinRequestStatus status,
        string decidedByUserId,
        DateTimeOffset decidedAt,
        CancellationToken ct);

    /// <summary>
    /// Resolves whatever pending request a person has in this community, if any. False when they had
    /// none.
    /// </summary>
    /// <remarks>
    /// Called when somebody becomes a member by a route that is not the queue — accepting an
    /// invitation while their request sits unread. Without it the request survives their joining, and
    /// the next officer to click approve hits the membership primary key.
    ///
    /// By (tenant, user, Pending) rather than by id, because the caller is the invitee and has no
    /// business knowing a request id; the filtered unique index guarantees there is at most one.
    /// </remarks>
    Task<bool> TryResolvePendingForUserAsync(
        Guid tenantId,
        string userId,
        JoinRequestStatus status,
        string decidedByUserId,
        DateTimeOffset decidedAt,
        CancellationToken ct);

    // Stages only.
    Task AddAsync(TenantJoinRequest request, CancellationToken ct);

    Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken ct);
}
