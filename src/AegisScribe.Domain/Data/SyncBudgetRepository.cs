using AegisScribe.Domain.Managers.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace AegisScribe.Domain.Data;

public class SyncBudgetRepository(AegisScribeDbContext db) : ISyncBudgetRepository
{
    public Task<TenantSyncBudgetWindow?> FindAsync(Guid tenantId, CancellationToken ct) =>
        db.TenantSyncBudgetWindows.AsNoTracking().FirstOrDefaultAsync(w => w.TenantId == tenantId, ct);

    public Task<bool> TryConsumeAsync(
        Guid tenantId,
        int calls,
        int limit,
        DateTimeOffset windowStart,
        DateTimeOffset now,
        CancellationToken ct) =>
        TryConsumeAsync(tenantId, calls, limit, windowStart, now, allowRetry: true, ct);

    private async Task<bool> TryConsumeAsync(
        Guid tenantId,
        int calls,
        int limit,
        DateTimeOffset windowStart,
        DateTimeOffset now,
        bool allowRetry,
        CancellationToken ct)
    {
        // One UPDATE, no prior SELECT. The WHERE decides whether the spend is allowed and the SET
        // applies it in the same statement, so two concurrent callers are serialised by the row lock
        // rather than by hope: the second one re-evaluates CallsConsumed as the first one left it.
        //
        // The window reset rides along in both halves. A row whose window has expired is treated as
        // empty by the WHERE and rewritten to `calls` by the SET — no separate "reset then spend" pass
        // that a concurrent caller could interleave with.
        var affected = await db.TenantSyncBudgetWindows
            .Where(w => w.TenantId == tenantId
                && (w.WindowStartedAt <= windowStart || w.CallsConsumed + calls <= limit))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(w => w.CallsConsumed, w => w.WindowStartedAt <= windowStart ? calls : w.CallsConsumed + calls)
                    .SetProperty(w => w.WindowStartedAt, w => w.WindowStartedAt <= windowStart ? now : w.WindowStartedAt),
                ct);

        if (affected > 0)
        {
            return true;
        }

        // Either the tenant has no row yet, or it has one and the budget is genuinely spent.
        if (await db.TenantSyncBudgetWindows.AnyAsync(w => w.TenantId == tenantId, ct))
        {
            // A row exists NOW, but may not have when the UPDATE above matched nothing. Concluding
            // "exhausted" from that would refuse a caller who raced the very first spend with a full
            // budget available — a 429 nobody can explain, appearing only under concurrency. Try the
            // UPDATE once more; if it still matches nothing, the budget genuinely is spent.
            return allowRetry
                && await TryConsumeAsync(tenantId, calls, limit, windowStart, now, allowRetry: false, ct);
        }

        if (calls > limit)
        {
            // A first spend larger than the whole window. Nothing to insert; it can never be granted.
            return false;
        }

        try
        {
            // TenantId is stamped by the SaveChanges interceptor, never assigned here (tenancy.md).
            db.TenantSyncBudgetWindows.Add(new TenantSyncBudgetWindow
            {
                Id = Guid.NewGuid(),
                WindowStartedAt = now,
                CallsConsumed = calls,
            });

            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException) when (allowRetry)
        {
            // Another request created the row between the check and the insert — the unique index on
            // TenantId is what makes that detectable rather than silently producing two windows.
            //
            // Exactly ONE retry, and the guard is load-bearing rather than defensive: a
            // DbUpdateException from any other cause leaves the row still absent, so an unguarded retry
            // would find nothing, insert again, fail again, and spin forever. That is a livelock rather
            // than a crash — no error, no progress, and a request that never returns.
            db.ChangeTracker.Clear();

            return await TryConsumeAsync(tenantId, calls, limit, windowStart, now, allowRetry: false, ct);
        }
    }
}
