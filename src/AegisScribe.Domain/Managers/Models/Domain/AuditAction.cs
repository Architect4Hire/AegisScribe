namespace AegisScribe.Domain.Managers.Models.Domain;

// What an audit row records. Persisted as its NAME, not its number (see AegisScribeDbContext's
// HasConversion<string>): audit rows outlive the code that wrote them, and an integer enum would make
// reordering or inserting a member a silent rewriting of history — the one thing an audit table must
// never permit.
//
// One member today, because 7.2b's officer clear is the first audited action in the repo. 7.4's
// roster writes, membership role changes and event locks each add their own.
public enum AuditAction
{
    // An officer freed somebody else's character claim. Frees it — never reassigns it.
    CharacterClaimCleared,

    // An officer made somebody else's roster entry an alt of another character (7.3), or detached one.
    // Written only when the actor does not claim the entry — a member reorganising their own
    // characters, or an officer reorganising theirs, is nobody else's business.
    RosterEntryAltLinked,
    RosterEntryAltUnlinked,

    // An officer changed somebody else's standing in the community (7.4). Same rule as the two above:
    // written only when the actor does not claim the entry, so an officer managing their own
    // characters leaves no trail and an officer managing yours always does.
    RosterEntryAdded,
    RosterEntryRankChanged,
    RosterEntryNoteChanged,
    RosterEntryRemoved,
}
