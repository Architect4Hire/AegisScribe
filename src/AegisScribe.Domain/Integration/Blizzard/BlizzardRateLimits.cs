namespace AegisScribe.Domain.Integration.Blizzard;

public static class BlizzardRateLimits
{
    // From Blizzard's Developer API Terms of Use: "You are limited to thirty-six thousand (36,000) calls
    // to the Blizzard Developer API per hour or such other limitation as Blizzard may deem appropriate."
    //
    // This is contractual, not advisory, so it is a constant rather than a setting — configuration may
    // size the limiter *under* it (see BlizzardOptions and the validation in
    // BlizzardServiceCollectionExtensions) but nothing may configure its way past it.
    public const int ContractualCallsPerHour = 36_000;

    // A per-second cap also exists. It is shown on the developer portal dashboard for our specific client
    // rather than in the public terms; 100/second is the figure developers consistently report, which
    // references/blizzard-terms-and-limits.md records as a working assumption to confirm on the dashboard
    // once credentials exist. The default burst below sits under it either way.
    public const int AssumedCallsPerSecond = 100;

    // What a configured limiter can spend in an hour at full tilt: the sustained rate for the whole hour,
    // plus one full bucket, since the bucket starts full and is spendable immediately on top of
    // replenishment.
    public static int WorstCaseCallsPerHour(int sustainedCallsPerSecond, int burstCallsPerSecond) =>
        (sustainedCallsPerSecond * 3_600) + burstCallsPerSecond;
}
