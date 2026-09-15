namespace AegisScribe.Domain.Integration;

// Bound from the "TenantSyncBudget" configuration section.
//
// The global rate limiter (BlizzardRateLimiter) enforces the contractual 36,000 calls an hour for the
// whole process. It says nothing about fairness: one community pressing "re-sync the roster" on 400
// members can spend a large slice of that budget and leave every other community queueing behind it.
// This is the fairness half (external.md -> "Sync is global; tenant-triggered work has a budget").
public sealed class TenantSyncBudgetOptions
{
    public const string SectionName = "TenantSyncBudget";

    // Blizzard calls one community may spend on work IT triggered, per window.
    //
    // Two thousand is about two full re-syncs of a very large guild. It would take several hundred
    // communities each exhausting themselves on the same day to approach the contractual daily ceiling,
    // so this is a fairness limit rather than a second cap on the app.
    //
    // Background refresh does NOT draw on this. The worker's passes are global, run once for everyone,
    // and are bounded by the shared limiter alone — charging them to a tenant would be charging a
    // community for work done on behalf of all of them.
    public int CallsPerWindow { get; set; } = 2_000;

    public TimeSpan Window { get; set; } = TimeSpan.FromDays(1);

    public bool IsValid => CallsPerWindow > 0 && Window > TimeSpan.Zero;
}
