namespace AegisScribe.Domain.Data;

// Everything the sync worker needs to refresh one character, and nothing else.
//
// A projection rather than a Character entity on purpose. The refresh only needs the three values a
// Blizzard profile request is built from, plus the realm id to upsert against; loading full entity
// graphs with equipment for a whole batch would cost far more and be thrown away immediately.
public sealed record StaleCharacterRef(Guid Id, Guid RealmId, string Region, string RealmSlug, string Name);
