namespace AegisScribe.Domain.Managers.Models.Domain;

// Removing a roster entry that other entries call their main is refused (7.4).
//
// Forced by the Restrict FK 7.3 put on MainRosterEntryId, but it would be the right answer anyway:
// detaching somebody's other characters as a side effect of removing one is the silent kind of damage,
// and the officer doing it cannot see what they are about to orphan. The same reasoning as refusing to
// delete a rank still in use.
//
// Two paths reach here. Business counts the alts first and reports the number, because "detach these
// two" is more actionable than "detach some". The foreign key catches the race Business cannot — an
// alt linked between the count and the delete — and has no count to give, so AltCount is null there
// rather than a zero that would read as a contradiction.
public class RosterEntryHasAltsException(int? altCount) : Exception(Describe(altCount))
{
    public int? AltCount { get; } = altCount;

    private static string Describe(int? altCount) => altCount is null
        ? "Characters are linked to this one as alts."
        : $"{altCount} characters are linked to this one as alts.";
}
