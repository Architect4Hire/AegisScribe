namespace AegisScribe.SyncWorker;

// Bound from the "SyncWorker" configuration section.
//
// Separate from BlizzardStalenessOptions, and the split is meaningful: that one says WHEN a row is out
// of compliance and is capped by the Terms of Use, this one says HOW FAST we work through the rows that
// are. Throughput knobs belong somewhere a performance change cannot accidentally relax a contractual
// term.
public sealed class SyncWorkerOptions
{
    public const string SectionName = "SyncWorker";

    // Characters per pass, at three Blizzard calls each — the summary, the equipment and the renders.
    //
    // Sized from the obligation rather than picked: holding N characters inside a seven-day window costs
    // N/7 refreshes a day, so 200 a run on the poll below sustains roughly 134,000 stored characters for
    // about 1,600 calls an hour — under 5% of the contractual 36,000.
    public int CharacterBatchSize { get; set; } = 200;

    public TimeSpan CharacterPollInterval { get; set; } = TimeSpan.FromMinutes(15);

    // Not a throughput control — BlizzardRateLimiter already holds outbound calls to 9/second. What this
    // bounds is queue occupancy: an unbounded fan-out parks a whole batch in a lease queue 200 deep and
    // a user's character lookup then waits behind it.
    //
    // It is also what keeps the DbContext story honest: the refresh loop opens one DI scope per
    // character because a DbContext is not thread-safe, and this caps how many are ever live at once.
    public int MaxConcurrentRefreshes { get; set; } = 4;

    // Guilds per pass. Far cheaper than characters — one call each, not two — so the batch can be larger
    // for the same spend.
    public int GuildBatchSize { get; set; } = 50;

    // Per pass, for EACH of the two media selections — characters missing renders and distinct items
    // missing icons — at one call each. So at most 2 × this per poll: 200 every five minutes is 2,400
    // calls an hour at the very worst, while a backlog drains, and close to nothing once it has.
    public int MediaBatchSize { get; set; } = 100;

    public TimeSpan MediaPollInterval { get; set; } = TimeSpan.FromMinutes(5);
}
