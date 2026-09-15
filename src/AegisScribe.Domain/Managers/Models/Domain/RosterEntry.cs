namespace AegisScribe.Domain.Managers.Models.Domain;

// This community's relationship with a character — the tenant-scoped half of tenancy.md's two-zone
// split: ONE global Character row, N RosterEntry rows pointing at it.
//
// The character is the fact about the world. Rank, note and join date are facts about THIS community,
// and two communities rostering the same character will disagree about all three while both being
// right. That is what stops tenant count from multiplying Blizzard API calls.
public class RosterEntry : ITenantScoped
{
    public Guid Id { get; set; }

    // Stamped by TenantStampingInterceptor on insert, never assigned by business code (tenancy.md).
    public Guid TenantId { get; set; }

    // The FK direction that is allowed: tenant-scoped holding a reference INTO the global zone. A
    // global entity must never hold one back the other way.
    public Guid CharacterId { get; set; }

    public Character Character { get; set; } = null!;

    // Nullable because a character can sit on the roster before anyone decides what they are. Points
    // at the community's OWN ladder, never at GuildMember.BlizzardRank.
    public Guid? TenantRankId { get; set; }

    public TenantRank? TenantRank { get; set; }

    // Who this character's main is, in THIS community. On RosterEntry and never on Character, because
    // the same player may be organised one way here and another way next door.
    //
    // Null means "this entry is a main". The model is deliberately ONE LEVEL DEEP: an entry whose
    // MainRosterEntryId is set may not be named as a main, and an entry that already has alts may not
    // become one. Those two rules foreclose cycles without a chain walk — every cycle needs a node
    // that is both a main and an alt.
    //
    // They are enforced in the WHERE of the UPDATE that writes this column
    // (RosterEntryRepository.TryLinkAltAsync), not by the reads in Business that precede it. Checked
    // separately they would be a read-then-write, and two concurrent opposite-direction links each
    // pass against pre-commit state and then write different rows — no conflict, and a cycle lands.
    public Guid? MainRosterEntryId { get; set; }

    public RosterEntry? MainRosterEntry { get; set; }

    // Officer-private, exposed behind TenantOfficer. Also externally-sourced text about a person, so
    // anything that later puts it near a prompt treats it as untrusted (ai.md).
    public string? OfficerNote { get; set; }

    public DateTimeOffset JoinedAt { get; set; }
}
