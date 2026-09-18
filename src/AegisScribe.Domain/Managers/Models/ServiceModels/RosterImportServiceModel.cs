namespace AegisScribe.Domain.Managers.Models.ServiceModels;

// What an import did, and the one thing it deliberately did NOT do.
//
// Import is purely additive: it inserts entries for guild members who are not on the roster yet, and
// it never updates or removes an existing row. A character who has left the guild since the last
// import is therefore still on the roster afterwards — usually correctly, because a gquit is very
// often an alt parked elsewhere, a member on a break, or somebody mid-transfer. Deleting the row
// would destroy the community's OWN data (its rank, its officer note, its alt link) on the strength
// of a fact the game reported about something else, and it would do it silently.
//
// So the import reports instead of acting, and NotInAnyLinkedGuild is that report.
public class RosterImportServiceModel
{
    // Entries created by this run. Zero on a re-import over unchanged data, which is what makes the
    // operation idempotent.
    public int Imported { get; set; }

    // Guild members already on the roster, left exactly as they were — rank and officer note intact.
    public int AlreadyOnRoster { get; set; }

    /// <summary>
    /// Roster entries whose character is in none of the guilds this community currently follows.
    /// </summary>
    /// <remarks>
    /// Named for what it actually measures rather than for "departed", because a character added by
    /// hand — a social, a friend's alt, an applicant — sits in this set too and was never in a guild
    /// to leave. Informational only: nothing here is removed by an import, and removing one stays a
    /// deliberate DELETE.
    /// </remarks>
    public IReadOnlyList<UnaffiliatedRosterEntryServiceModel> NotInAnyLinkedGuild { get; set; } = [];

    // The full count, since the list above is capped — a long-running community can accumulate more
    // of these than any response should carry (api-contract.md: every list has a server-enforced
    // maximum).
    public int NotInAnyLinkedGuildCount { get; set; }
}

// Just enough to name the row on screen and link to it. The full entry is one roster read away.
public class UnaffiliatedRosterEntryServiceModel
{
    public Guid RosterEntryId { get; set; }
    public string CharacterName { get; set; } = string.Empty;
    public string RealmSlug { get; set; } = string.Empty;
}
