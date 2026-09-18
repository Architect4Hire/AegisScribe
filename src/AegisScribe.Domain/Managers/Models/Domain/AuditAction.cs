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

    // The membership lifecycle (auth.md). Every path into and out of a community is audited without
    // exception — unlike the roster actions above, which are audited only when the actor was not the
    // subject. There is no "managing your own" case here: changing a role or removing somebody is
    // always an officer acting on another person's standing.
    MemberInvited,
    InvitationRevoked,

    // The invitee's own acts. InvitationRefused is written when somebody presents a token belonging to
    // this community that is expired, already spent, or revoked — "someone tried the link after I
    // killed it" is a question an officer will ask. It cannot be written for a token that never
    // existed: there is no community to write it to.
    InvitationAccepted,
    InvitationRefused,
    JoinRequestApproved,
    JoinRequestDeclined,
    MemberRoleChanged,
    MemberRemoved,

    // Distinct from MemberRemoved even though the rows they leave behind are identical, because the
    // question an audit trail gets asked is "did somebody push them out, or did they walk" — and
    // deriving that later from ActorUserId == SubjectUserId would mean trusting that nobody ever
    // reuses the removal path for a self-service leave.
    MemberLeft,

    // One row for a whole import, not one per character. The rule above — audit an officer reaching
    // into somebody else's data — has no single subject to point at here, and four hundred rows for
    // one button press would bury the ones that do name a person.
    RosterImportedFromGuild,
}
