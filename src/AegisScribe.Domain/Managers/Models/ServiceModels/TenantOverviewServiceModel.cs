namespace AegisScribe.Domain.Managers.Models.ServiceModels;

// What a community HAS, in one read — the five facts the first-run checklist is a pure function of.
//
// Composed on purpose rather than left as four list calls the client counts itself: this is the first
// screen a brand-new community ever renders, and it is the one place five round trips would be most
// visible (api-contract.md → "no chatty screens"). Counting in SQL also means a 400-character roster
// costs the same as an empty one.
//
// Nothing here is a stored progress marker, and there must never be one. Every field is a fact about
// the present state of the community, so deleting a rank correctly un-completes that step; a persisted
// "step 3 of 4" column would start lying the first time an officer changed their mind.
public class TenantOverviewServiceModel
{
    /// <summary>Whether this community follows at least one guild.</summary>
    public bool HasLinkedGuild { get; set; }

    public int RankCount { get; set; }

    public int RosterCount { get; set; }

    /// <summary>People in this community, the caller included — so a fresh one reads 1, never 0.</summary>
    public int MemberCount { get; set; }

    // Whether this DEPLOYMENT has Blizzard credentials, not anything about this community. Credentials
    // are optional by design and the gateway degrades to a no-op without them (CLAUDE.md → Usage), so
    // the guild step needs to say that plainly up front rather than let an officer type a guild name
    // and hit a failure.
    public bool BlizzardConfigured { get; set; }
}
