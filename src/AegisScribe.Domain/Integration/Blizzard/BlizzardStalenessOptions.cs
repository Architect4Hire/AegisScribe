namespace AegisScribe.Domain.Integration.Blizzard;

// Bound from the "Blizzard:Staleness" configuration section.
//
// This is not a performance tuning knob wearing a compliance hat — it is the other way round. The
// Blizzard Developer API Terms of Use permit storing their data on the condition that it is refreshed
// "no less frequently than every thirty (30) days", so every value here is a compliance control that
// happens to also affect latency. BlizzardServiceCollectionExtensions validates the cap at startup and
// BlizzardStalenessOptionsTests asserts it, because this is the setting most likely to be quietly
// raised by someone chasing sync throughput.
public sealed class BlizzardStalenessOptions
{
    public const string SectionName = "Blizzard:Staleness";

    // The hard ceiling from the Terms of Use. Nothing in this class may exceed it, and the constant
    // lives here rather than inline so the number has one home to change if the terms ever do.
    public static readonly TimeSpan MaximumRefreshInterval = TimeSpan.FromDays(30);

    // How long a stored character may go unrefreshed before a read tries Blizzard again.
    //
    // Seven days, not thirty. references/blizzard-terms-and-limits.md asks for a margin deliberately:
    // refreshing at the deadline means any worker outage longer than a day puts us out of compliance,
    // whereas refreshing weekly leaves three weeks of slack to notice and fix one.
    //
    // Applied to the equipment snapshot as well as the character row. They are separate endpoints with
    // separate LastSyncedAt columns, but a read refreshes them together, so a second threshold would be
    // two names for one decision.
    public TimeSpan CharacterRefreshAfter { get; set; } = TimeSpan.FromDays(7);

    // How long the realm catalogue may go unrefreshed before the worker runs another pass (6.4b).
    //
    // A separate knob from the one above rather than a reuse of it, because it answers a different
    // question: that one is per character row, this one gates a whole-region pass. It is also the
    // restart gate — `aspire run` restarts the worker constantly, and without this every restart would
    // spend ~100 calls re-fetching a catalogue that has not changed.
    //
    // Realms change rarely, so this could sit much higher; it does not, because the 30-day obligation
    // applies to realm rows exactly as it does to characters and the margin argument is the same.
    public TimeSpan RealmRefreshAfter { get; set; } = TimeSpan.FromDays(7);
}
