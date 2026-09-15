namespace AegisScribe.Domain.Business;

// A community has spent its sync budget for the current window. Carries the retry hint because the
// alternatives are worse: a silent queue tells the user their sync worked when it has not started, and
// a bare 429 produces the retry storm the limit exists to prevent (external.md).
public sealed class SyncBudgetExhaustedException(TimeSpan retryAfter, int limit)
    : Exception($"This community has used its sync budget of {limit} Blizzard calls for the current window.")
{
    public TimeSpan RetryAfter { get; } = retryAfter;

    public int Limit { get; } = limit;
}
