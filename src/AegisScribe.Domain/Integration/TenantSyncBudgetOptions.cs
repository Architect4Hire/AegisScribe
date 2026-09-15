namespace AegisScribe.Domain.Integration;

// Bound from the "TenantSyncBudget" configuration section.
//
// BlizzardRateLimiter enforces the contractual cap for the whole process and says nothing about
// fairness: one community re-syncing 400 members can leave every other community queueing behind it.
// This is the fairness half (external.md).
public sealed class TenantSyncBudgetOptions
{
    public const string SectionName = "TenantSyncBudget";

    // Blizzard calls one community may spend on work IT triggered, per window. Two thousand is about
    // two full re-syncs of a very large guild — a fairness limit rather than a second cap on the app.
    //
    // Background refresh does NOT draw on this: the worker's passes run once for everyone, so charging
    // them to a tenant would bill one community for work done on behalf of all of them.
    public int CallsPerWindow { get; set; } = 2_000;

    public TimeSpan Window { get; set; } = TimeSpan.FromDays(1);

    public bool IsValid => CallsPerWindow > 0 && Window > TimeSpan.Zero;
}
