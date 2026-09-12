namespace AegisScribe.Domain.Managers.Models.Domain;

public class GuildMember
{
    public Guid Id { get; set; }
    public Guid GuildId { get; set; }
    public Guild Guild { get; set; } = null!;
    public Guid CharacterId { get; set; }
    public Character Character { get; set; } = null!;

    // What the GAME says (0-9, straight from Blizzard's guild roster endpoint) — not the
    // community's own rank. That's RosterEntry.TenantRankId -> TenantRank (Phase 7), a
    // tenant-scoped, user-defined ladder ("Raider", "Trial", "Social"). Neither derives from
    // the other: a community may run two raid teams inside one in-game guild, or span two
    // guilds, so the game's rank number and the community's rank name are unrelated facts.
    public int BlizzardRank { get; set; }
}
