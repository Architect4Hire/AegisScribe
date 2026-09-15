using AegisScribe.Domain.Managers.Models.Domain;

namespace AegisScribe.Domain.Data;

public class SyncBudgetDataLayer(ISyncBudgetRepository repository) : ISyncBudgetDataLayer
{
    public Task<bool> TryConsumeAsync(
        Guid tenantId, int calls, int limit, DateTimeOffset windowStart, DateTimeOffset now, CancellationToken ct) =>
        repository.TryConsumeAsync(tenantId, calls, limit, windowStart, now, ct);

    public Task<TenantSyncBudgetWindow?> FindAsync(Guid tenantId, CancellationToken ct) =>
        repository.FindAsync(tenantId, ct);
}
