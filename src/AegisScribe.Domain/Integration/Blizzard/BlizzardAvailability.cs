namespace AegisScribe.Domain.Integration.Blizzard;

public enum BlizzardAvailability
{
    // No client id and secret configured. A supported state rather than a fault — the app serves
    // stored and seeded data (external.md).
    NotConfigured,

    // Credentials are configured but Blizzard would not answer: it rejected them, or it is unreachable.
    Unavailable,

    // Credentials minted a token and Blizzard answered a real request.
    Available,
}
