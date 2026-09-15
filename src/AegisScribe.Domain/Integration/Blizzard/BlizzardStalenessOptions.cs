namespace AegisScribe.Domain.Integration.Blizzard;

// Bound from the "Blizzard:Staleness" configuration section.
//
// Not a performance tuning knob wearing a compliance hat — the other way round. The Terms of Use permit
// storing Blizzard's data on the condition that it is refreshed "no less frequently than every thirty
// (30) days", so every value here is a compliance control that happens to also affect latency.
// BlizzardServiceCollectionExtensions validates the cap at startup, because this is the setting most
// likely to be quietly raised by someone chasing sync throughput.
public sealed class BlizzardStalenessOptions
{
    public const string SectionName = "Blizzard:Staleness";

    // The hard ceiling from the Terms of Use. Nothing in this class may exceed it.
    public static readonly TimeSpan MaximumRefreshInterval = TimeSpan.FromDays(30);

    // Seven days, not thirty: refreshing at the deadline means any worker outage longer than a day puts
    // us out of compliance, whereas weekly leaves three weeks of slack to notice and fix one.
    //
    // Applied to the equipment snapshot as well as the character row. They are separate endpoints with
    // separate LastSyncedAt columns, but a read refreshes them together, so a second threshold would be
    // two names for one decision.
    public TimeSpan CharacterRefreshAfter { get; set; } = TimeSpan.FromDays(7);

    // A separate knob rather than a reuse of the one above, because it answers a different question:
    // that one is per character row, this gates a whole-region pass. It is also the restart gate —
    // `aspire run` restarts the worker constantly, and every restart would otherwise spend ~100 calls
    // re-fetching an unchanged catalogue.
    //
    // Realms change rarely, so this could sit much higher; it does not, because the 30-day obligation
    // applies to realm rows exactly as it does to characters.
    public TimeSpan RealmRefreshAfter { get; set; } = TimeSpan.FromDays(7);
}
