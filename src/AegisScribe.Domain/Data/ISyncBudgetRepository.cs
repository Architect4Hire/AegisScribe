using AegisScribe.Domain.Managers.Models.Domain;

namespace AegisScribe.Domain.Data;

public interface ISyncBudgetRepository
{
    // Spends `calls` against the tenant's current window, atomically, and says whether it was allowed.
    //
    // Atomically is the whole contract. A read-then-write here would let two officers pressing re-sync
    // at the same moment each see the budget as available and each spend it, which is the one failure
    // this class exists to prevent.
    Task<bool> TryConsumeAsync(Guid tenantId, int calls, int limit, DateTimeOffset windowStart, DateTimeOffset now, CancellationToken ct);

    // The current window row, or null when this tenant has never spent anything. Null is a real answer
    // meaning "a full budget", not a missing row to repair.
    Task<TenantSyncBudgetWindow?> FindAsync(Guid tenantId, CancellationToken ct);
}
