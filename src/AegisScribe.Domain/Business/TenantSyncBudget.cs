using AegisScribe.Domain.Data;
using AegisScribe.Domain.Integration;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using Microsoft.Extensions.Options;

namespace AegisScribe.Domain.Business;

// The per-tenant ceiling on tenant-TRIGGERED Blizzard work (external.md -> "Sync is global;
// tenant-triggered work has a budget").
//
// The distinction that matters: the shared BlizzardRateLimiter enforces the contractual 36,000 calls an
// hour for the whole process, and is indifferent to who spends them. This decides whether one community
// may keep spending while others are waiting. Without it, a 400-member guild pressing "re-sync" is
// perfectly within the global cap and still starves every other community for minutes.
//
// Background refresh never draws on this. The worker's passes are global — run once on behalf of
// everyone — so charging them to whichever tenant happens to roster the character would bill a
// community for work done for all of them.
public interface ITenantSyncBudget
{
    // Spends `calls` or throws SyncBudgetExhaustedException with a retry hint. Deliberately not a
    // bool: a caller that forgets to check a bool spends the budget anyway, and this is the one place
    // where "failed open" is the wrong default.
    Task ConsumeAsync(Guid tenantId, int calls, CancellationToken ct);

    Task<SyncBudgetServiceModel> GetAsync(Guid tenantId, CancellationToken ct);
}

public class TenantSyncBudget(
    ISyncBudgetDataLayer dataLayer,
    IOptions<TenantSyncBudgetOptions> options,
    TimeProvider timeProvider) : ITenantSyncBudget
{
    public async Task ConsumeAsync(Guid tenantId, int calls, CancellationToken ct)
    {
        var settings = options.Value;
        var now = timeProvider.GetUtcNow();

        var granted = await dataLayer.TryConsumeAsync(
            tenantId,
            calls,
            settings.CallsPerWindow,
            windowStart: now - settings.Window,
            now,
            ct);

        if (granted)
        {
            return;
        }

        // Re-read only on the denied path, to say WHEN rather than just "no". The read is not part of
        // the accounting decision — that was made atomically above — so a window that rolls over
        // between the two costs nothing worse than a retry hint of zero.
        var window = await dataLayer.FindAsync(tenantId, ct);

        throw new SyncBudgetExhaustedException(RetryAfter(window, settings, now), settings.CallsPerWindow);
    }

    public async Task<SyncBudgetServiceModel> GetAsync(Guid tenantId, CancellationToken ct)
    {
        var settings = options.Value;
        var now = timeProvider.GetUtcNow();
        var window = await dataLayer.FindAsync(tenantId, ct);

        // No row, or a row whose window has rolled over, both mean a full budget. A tenant that has
        // never triggered a sync has no row, and that is a normal state rather than one to repair.
        var spent = window is null || window.WindowStartedAt <= now - settings.Window
            ? 0
            : window.CallsConsumed;

        return new SyncBudgetServiceModel
        {
            CallsPerWindow = settings.CallsPerWindow,
            CallsConsumed = spent,
            CallsRemaining = Math.Max(0, settings.CallsPerWindow - spent),
            WindowResetsAt = spent == 0 ? now : window!.WindowStartedAt + settings.Window,
        };
    }

    private static TimeSpan RetryAfter(
        Managers.Models.Domain.TenantSyncBudgetWindow? window, TenantSyncBudgetOptions settings, DateTimeOffset now)
    {
        if (window is null)
        {
            // Denied with no row at all means a single request asked for more than a whole window.
            // Waiting will not help, so the hint is the window itself rather than a misleading zero.
            return settings.Window;
        }

        var remaining = window.WindowStartedAt + settings.Window - now;

        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }
}
