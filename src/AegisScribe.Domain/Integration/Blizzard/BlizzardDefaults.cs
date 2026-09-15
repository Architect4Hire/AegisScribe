namespace AegisScribe.Domain.Integration.Blizzard;

public static class BlizzardDefaults
{
    // The token endpoint gets its own HttpClient, separate from the gateway's typed client, because
    // that one carries BlizzardAuthHandler — minting a token through it would call straight back into
    // the token provider.
    public const string OAuthHttpClientName = "blizzard-oauth";

    // How long an availability answer is reused. Long enough that health polling cannot meaningfully
    // spend the 36,000 calls/hour budget (blizzard-terms-and-limits.md), short enough that credentials
    // which start working show up without a restart.
    public static readonly TimeSpan AvailabilityCacheDuration = TimeSpan.FromMinutes(5);
}
