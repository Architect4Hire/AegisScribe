using AegisScribe.Domain.Managers.Models.Domain;

namespace AegisScribe.Domain.Data;

// Pass-throughs today, and correct as such (add-endpoint skill step 6): each operation is one
// repository call. The seam is what lets the budget grow a second store — a cached "remaining" read,
// say — without Business learning about it.
public interface ISyncBudgetDataLayer
{
    Task<bool> TryConsumeAsync(Guid tenantId, int calls, int limit, DateTimeOffset windowStart, DateTimeOffset now, CancellationToken ct);

    Task<TenantSyncBudgetWindow?> FindAsync(Guid tenantId, CancellationToken ct);
}
