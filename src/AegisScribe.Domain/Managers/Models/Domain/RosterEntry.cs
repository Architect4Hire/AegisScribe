namespace AegisScribe.Domain.Managers.Models.Domain;

// This community's relationship with a character (7.2) — the tenant-scoped half of the split
// tenancy.md spends its two-zone section building up to: ONE global Character row, N RosterEntry rows
// pointing at it.
//
// The character is the fact about the world — the same level, gear and item level however many
// communities care about it. Rank, note and join date are facts about THIS community, and two
// communities rostering the same character will disagree about all three while both being right.
// That disagreement is the test that puts this row here and the Character row in the global zone, and
// it is what stops tenant count from multiplying Blizzard API calls.
public class RosterEntry : ITenantScoped
{
    public Guid Id { get; set; }

    // Stamped by TenantStampingInterceptor on insert, never assigned by business code (tenancy.md).
    public Guid TenantId { get; set; }

    // The FK direction that is allowed: tenant-scoped holding a reference INTO the global zone. A
    // global entity must never hold one back the other way — that would make shared reference data
    // depend on one community's rows and break every other community when they are deleted.
    public Guid CharacterId { get; set; }

    public Character Character { get; set; } = null!;

    // Nullable because a character can sit on the roster before anyone decides what they are. Points
    // at the community's OWN ladder (TenantRank), never at GuildMember.BlizzardRank — what the game
    // says and what the community says are different things and neither derives from the other.
    public Guid? TenantRankId { get; set; }

    public TenantRank? TenantRank { get; set; }

    // Who this character's main is, in THIS community (7.3). On RosterEntry and never on Character,
    // because who is somebody's main is a community's judgement — the same player may be organised one
    // way here and another way next door, and both are right.
    //
    // Null means "this entry is a main" (or simply unlinked). The model is deliberately ONE LEVEL
    // DEEP: an entry whose MainRosterEntryId is set may not itself be named as a main, and an entry
    // that already has alts may not become one. Those two rules are what foreclose cycles without a
    // chain walk — every cycle needs a node that is both a main and an alt.
    //
    // They are enforced in the WHERE of the UPDATE that writes this column
    // (RosterEntryRepository.TryLinkAltAsync), NOT by the reads in Business that precede it. Checked
    // separately they would be a read-then-write, and two concurrent opposite-direction links each
    // pass their own check against pre-commit state and then write different rows — no conflict, and a
    // cycle lands. The rules are only actually invariants because the check and the write are one
    // statement.
    public Guid? MainRosterEntryId { get; set; }

    public RosterEntry? MainRosterEntry { get; set; }

    // Officer-private. Reads of the roster are TenantMember, so this deliberately does not appear on
    // RosterEntryServiceModel — 7.4 exposes it behind TenantOfficer. It is also externally-sourced
    // text about a person, so anything that later puts it near a prompt treats it as untrusted
    // (ai.md).
    public string? OfficerNote { get; set; }

    public DateTimeOffset JoinedAt { get; set; }
}
