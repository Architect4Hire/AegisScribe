namespace AegisScribe.Domain.Managers.Models.Domain;

// What a roster may be ordered by (7.4). A closed whitelist rather than a free-text column name: this
// value reaches an ORDER BY, and the one rule this repo will not bend is that a caller — human or
// model — never gets to name a column (CLAUDE.md, Restrictions).
//
// Each option carries a FIXED direction, because the direction that makes sense is a property of the
// field rather than a preference: nobody sorts a roster worst-geared-first, which is why screen S3
// draws item level with a ↓. A `dir` parameter can be added later without breaking anything; guessing
// now would double the keyset predicates for a control no screen has.
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
