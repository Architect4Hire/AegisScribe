namespace AegisScribe.Domain.Integration.Blizzard;

public static class BlizzardRateLimits
{
    // From the Developer API Terms of Use. Contractual, not advisory, so it is a constant rather than a
    // setting: configuration may size the limiter *under* it but nothing may configure its way past it.
    public const int ContractualCallsPerHour = 36_000;

    // A per-second cap also exists, shown on the developer portal dashboard rather than in the public
    // terms. 100/second is what developers consistently report — a working assumption to confirm on the
    // dashboard once credentials exist. The default burst sits under it either way.
    public const int AssumedCallsPerSecond = 100;

    // What a configured limiter can spend in an hour at full tilt: the sustained rate for the hour plus
    // one full bucket, since the bucket starts full and is spendable on top of replenishment.
    public static int WorstCaseCallsPerHour(int sustainedCallsPerSecond, int burstCallsPerSecond) =>
        (sustainedCallsPerSecond * 3_600) + burstCallsPerSecond;
}
