namespace AegisScribe.Domain.Integration.Blizzard;

// Holds the last availability answer. This is a singleton and separate from the gateway on purpose:
// AddHttpClient<IBlizzardGateway, BlizzardGateway> registers a typed client, which is *transient*, so a
// cache living on the gateway would be discarded after every call and would quietly spend a Blizzard
// request on every health poll instead of one per window.
//
// No lock. Two concurrent probes cost one extra call and settle on the same answer, which is cheaper
// than serialising every reader.
public sealed class BlizzardAvailabilityCache(TimeProvider timeProvider)
{
    private volatile Entry? _entry;

    public bool TryGet(out BlizzardAvailability availability)
    {
        var entry = _entry;

        if (entry is not null && timeProvider.GetUtcNow() < entry.ExpiresAt)
        {
            availability = entry.Availability;
            return true;
        }

        availability = default;
        return false;
    }

    public void Set(BlizzardAvailability availability) =>
        _entry = new Entry(availability, timeProvider.GetUtcNow() + BlizzardDefaults.AvailabilityCacheDuration);

    private sealed record Entry(BlizzardAvailability Availability, DateTimeOffset ExpiresAt);
}
