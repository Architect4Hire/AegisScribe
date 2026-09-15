namespace AegisScribe.Domain.Managers.Models.Domain;

// Removing a roster entry that other entries call their main is refused: detaching somebody's other
// characters as a side effect is the silent kind of damage, and the officer cannot see what they would
// orphan.
//
// Two paths reach here. Business counts the alts first, because "detach these two" is more actionable
// than "detach some". The foreign key catches the race Business cannot — an alt linked between the
// count and the delete — and has no count to give, so AltCount is null there rather than a zero.
public class RosterEntryHasAltsException(int? altCount) : Exception(Describe(altCount))
{
    public int? AltCount { get; } = altCount;

    private static string Describe(int? altCount) => altCount is null
        ? "Characters are linked to this one as alts."
        : $"{altCount} characters are linked to this one as alts.";
}
