namespace AegisScribe.Domain.Integration.Blizzard;

// Thrown when a Blizzard read could not be answered — no credentials, a transport failure, a timeout, a
// self-imposed rate-limit short-circuit, a non-404 status, or a body we could not map.
//
// The distinction this type preserves: a 404 returns null, because a character that does not exist is a
// normal answer, and everything else throws. Collapsing the two would let the cache-first read treat an
// outage as "no such character" and hide a live character behind an empty page.
public sealed class BlizzardUnavailableException : Exception
{
    public BlizzardUnavailableException(string message)
        : base(message)
    {
    }

    public BlizzardUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
