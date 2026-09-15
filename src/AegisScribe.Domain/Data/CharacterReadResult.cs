using AegisScribe.Domain.Managers.Models.Domain;

namespace AegisScribe.Domain.Data;

// What a cache-first read answers with: the character, and whether what came back is known to be out of
// date.
//
// The flag cannot be derived from the entity alone, which is why this type exists rather than the read
// returning a bare Character. LastSyncedAt says when the row was written, not whether we just tried to
// refresh it and failed — and a caller comparing it against the staleness policy would be re-deciding a
// compliance question that belongs in one place.
public sealed record CharacterReadResult(Character? Character, bool IsDegraded)
{
    public static readonly CharacterReadResult NotFound = new(null, false);

    // Stored data, refreshed or fresh enough not to need it.
    public static CharacterReadResult Current(Character character) => new(character, false);

    // Stored data we could not refresh: Blizzard unreachable, throttled, unconfigured, or no longer
    // aware of this character. Never "not found", and never silently presented as current.
    public static CharacterReadResult Stale(Character? character) => new(character, character is not null);
}
