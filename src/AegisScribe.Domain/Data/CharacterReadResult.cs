using AegisScribe.Domain.Managers.Models.Domain;

namespace AegisScribe.Domain.Data;

// What a cache-first read answers with: the character, and whether what came back is known to be out of
// date.
//
// The flag cannot be derived from the entity alone, which is why this type exists rather than the read
// returning a bare Character. LastSyncedAt says when the row was written; it does not say whether we
// just tried to refresh it and failed. A caller comparing LastSyncedAt against the staleness policy
// itself would also be re-deciding a compliance question that belongs in one place (external.md), and
// would get the wrong answer in the case that matters most — a row refreshed seconds ago from a
// Blizzard that then went down is fresh, and a row we could not refresh is not.
//
// IsDegraded is what CharacterDetailServiceModel.IsDegraded carries to the UI, and 5.7 already built
// the screen state for it.
public sealed record CharacterReadResult(Character? Character, bool IsDegraded)
{
    public static readonly CharacterReadResult NotFound = new(null, false);

    // Stored data, refreshed or fresh enough not to need it.
    public static CharacterReadResult Current(Character character) => new(character, false);

    // Stored data we could not refresh: Blizzard was unreachable, throttled, unconfigured, or no longer
    // knows this character. Never "not found" — a stale answer beats no answer (add-endpoint skill,
    // step 6) — and never silently presented as current.
    public static CharacterReadResult Stale(Character? character) => new(character, character is not null);
}
