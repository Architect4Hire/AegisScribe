namespace Microsoft.Extensions.Hosting;

public static class HealthCheckTags
{
    // Liveness. Checks tagged with this answer "is the process responsive", nothing more.
    public const string Live = "live";

    // A third-party dependency this app degrades gracefully without — the Blizzard API, say. These are
    // reported at /health/external and deliberately excluded from /health, because Aspire gates resource
    // readiness on /health and an unconfigured external credential must not make a service look broken.
    // It still serves stored data.
    public const string External = "external";
}
