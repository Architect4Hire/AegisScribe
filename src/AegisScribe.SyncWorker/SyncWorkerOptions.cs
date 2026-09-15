namespace AegisScribe.SyncWorker;

// Bound from the "SyncWorker" configuration section.
//
// Separate from BlizzardStalenessOptions, and the split is meaningful: that one says WHEN a row is out
// of compliance and is capped by the Terms of Use, this one says HOW FAST we are willing to work
// through the rows that are. Throughput knobs belong somewhere a performance change cannot accidentally
// relax a contractual term.
public sealed class SyncWorkerOptions
{
    public const string SectionName = "SyncWorker";

    // Characters refreshed per pass. Each costs two Blizzard calls — the summary and the equipment —
    // so this is 400 calls a run.
    //
    // Sized from the obligation rather than picked: holding N characters inside a seven-day window
    // costs N/7 refreshes a day. At 200 a run on the poll below, the worker sustains roughly 134,000
    // stored characters while spending about 1,600 calls an hour — under 5% of the contractual 36,000,
    // which leaves the rest for the people actually using the app.
    public int CharacterBatchSize { get; set; } = 200;

    public TimeSpan CharacterPollInterval { get; set; } = TimeSpan.FromMinutes(15);

    // Not a throughput control — BlizzardRateLimiter already holds outbound calls to 9/second. What this
    // bounds is queue occupancy: an unbounded fan-out parks a whole batch in a lease queue 200 deep
    // (BlizzardOptions.MaxQueuedCalls) and a user's character lookup then waits behind the batch.
    //
    // It is also what keeps the DbContext story honest. The refresh loop opens one DI scope per
    // character because a DbContext is not thread-safe, and this caps how many are ever live at once.
    public int MaxConcurrentRefreshes { get; set; } = 4;

    // Guilds refreshed per pass. Far cheaper than characters — one call each, not two — so the batch
    // can be larger for the same spend. Most deployments will have fewer linked guilds than this and
    // the pass will simply select what exists.
    public int GuildBatchSize { get; set; } = 50;
}
