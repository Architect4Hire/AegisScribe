namespace AegisScribe.Domain.Managers.Models.Domain;

// A closed whitelist rather than a free-text column name: this value reaches an ORDER BY, and no
// caller — human or model — ever gets to name a column (CLAUDE.md, Restrictions).
//
// Each option carries a FIXED direction, because the sensible direction is a property of the field
// rather than a preference: nobody sorts a roster worst-geared-first. A `dir` parameter can be added
// later without breaking anything.
public enum RosterSort
{
    // Rank order ascending, then character name. The ladder as the community defined it, which is what
    // a roster is for.
    Rank,

    // Character name, A→Z.
    Name,

    // Item level, HIGHEST first.
    ItemLevel,
}
