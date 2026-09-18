using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;

namespace AegisScribe.Domain.Data;

// TenantMembership is not ITenantScoped (see the entity), so there is no ambient filter to lean on and
// EVERY method here names its tenant. That parameter is always ITenantContext.TenantId, resolved from
// the route — never a value from the client (tenancy.md).
public interface ITenantMembershipRepository
{
    /// <summary>The community's members, newest first, keyset-resumed from <paramref name="afterId"/>.</summary>
    Task<IReadOnlyList<TenantMemberServiceModel>> ListAsync(
        Guid tenantId, DateTimeOffset? afterJoinedAt, string? afterId, int take, CancellationToken ct);

    /// <summary>How many people belong to this community, the caller included.</summary>
    Task<int> CountAsync(Guid tenantId, CancellationToken ct);

    Task<TenantMembership?> FindAsync(Guid tenantId, string userId, CancellationToken ct);

    /// <summary>
    /// Changes a member's role, refusing to demote the community's last <see cref="TenantRole.Owner"/>.
    /// Returns false when the last-owner rule refused it.
    /// </summary>
    /// <remarks>
    /// The rule lives in this statement's WHERE rather than a SELECT before it, and that is
    /// load-bearing. Checked separately it is a read-then-write: two concurrent demotes of the last
    /// TWO owners each pass against pre-commit state and then write DIFFERENT rows, so nothing
    /// conflicts and the community lands with no owner at all. Same shape as
    /// RosterEntryRepository.TryLinkAltAsync and SyncBudgetRepository.TryConsumeAsync.
    /// </remarks>
    Task<bool> TrySetRoleAsync(Guid tenantId, string userId, TenantRole role, CancellationToken ct);

    /// <summary>
    /// Removes a membership, refusing to remove the community's last <see cref="TenantRole.Owner"/>.
    /// Returns false when the last-owner rule refused it.
    /// </summary>
    /// <remarks>Guard in the WHERE, for the same reason as <see cref="TrySetRoleAsync"/>.</remarks>
    Task<bool> TryRemoveAsync(Guid tenantId, string userId, CancellationToken ct);

    // Stages only — a membership created by an invite or an approval shares a transaction with the
    // audit row and, for an approval, with the decided request.
    Task AddAsync(TenantMembership membership, CancellationToken ct);

    Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken ct);
}
