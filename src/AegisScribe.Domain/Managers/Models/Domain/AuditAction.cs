namespace AegisScribe.Domain.Managers.Models.Domain;

// Persisted as its NAME, not its number: audit rows outlive the code that wrote them, and an integer
// enum would make reordering or inserting a member a silent rewriting of history.
public enum AuditAction
{
    // An officer freed somebody else's character claim. Frees it — never reassigns it.
    CharacterClaimCleared,

    // Written only when the actor does not claim the entry — reorganising your own characters is
    // nobody else's business.
    RosterEntryAltLinked,
    RosterEntryAltUnlinked,

    // An officer changed somebody else's standing in the community. Same rule as the two above.
    RosterEntryAdded,
    RosterEntryRankChanged,
    RosterEntryNoteChanged,
    RosterEntryRemoved,
}
