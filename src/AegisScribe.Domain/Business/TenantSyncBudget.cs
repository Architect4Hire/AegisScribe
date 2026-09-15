using AegisScribe.Domain.Data;
using AegisScribe.Domain.Integration;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using Microsoft.Extensions.Options;

namespace AegisScribe.Domain.Business;

// The per-tenant ceiling on tenant-TRIGGERED Blizzard work (external.md).
//
// The distinction that matters: BlizzardRateLimiter enforces the contractual cap for the whole process
// and is indifferent to who spends it. This decides whether one community may keep spending while
// others wait — without it, a 400-member guild pressing "re-sync" is within the global cap and still
// starves everyone else for minutes.
//
// Background refresh never draws on this: the worker's passes are global, so charging them to whichever
// tenant happens to roster the character would bill one community for work done for all of them.
public interface ITenantSyncBudget
{
    // Throws rather than returning a bool: a caller that forgets to check a bool spends the budget
    // anyway, and this is the one place where failing open is the wrong default.
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

        // Re-read only on the denied path, to say WHEN rather than just "no". Not part of the
        // accounting decision, which was made atomically above.
        var window = await dataLayer.FindAsync(tenantId, ct);

        throw new SyncBudgetExhaustedException(RetryAfter(window, settings, now), settings.CallsPerWindow);
    }

    public async Task<SyncBudgetServiceModel> GetAsync(Guid tenantId, CancellationToken ct)
    {
        var settings = options.Value;
        var now = timeProvider.GetUtcNow();
        var window = await dataLayer.FindAsync(tenantId, ct);

        // No row, or a rolled-over window, both mean a full budget. A tenant that has never triggered a
        // sync has no row, which is normal rather than something to repair.
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
            // Denied with no row means a single request asked for more than a whole window. Waiting
            // will not help, so the hint is the window itself rather than a misleading zero.
            return settings.Window;
        }

        var remaining = window.WindowStartedAt + settings.Window - now;

        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }
}
