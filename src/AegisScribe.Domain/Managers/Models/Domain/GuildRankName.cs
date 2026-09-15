namespace AegisScribe.Domain.Managers.Models.Domain;

// What this community calls a guild's in-game ranks. It exists because Blizzard does not give us the
// names: the roster endpoint returns the rank as a NUMBER, 0-9, and the Game Data API exposes no names
// at all, so "rank 3 is Veteran" is something a person types.
//
// TENANT-SCOPED, and the rules decide that rather than taste. The global zone is defined as *public
// Blizzard data*, which a name somebody typed fails outright; and an officer editing a global row has
// no policy that could authorize it, since tenant membership is the only authorization model here.
//
// Three rank concepts now, and none derives from another:
//   GuildMember.BlizzardRank  — the number the game reports.
//   GuildRankName             — what this community calls that number.
//   TenantRank                — the community's OWN ladder, unrelated to the guild's.
public class GuildRankName : ITenantScoped
{
    public Guid Id { get; set; }

    // Stamped by TenantStampingInterceptor on insert, never assigned by business code (tenancy.md).
    public Guid TenantId { get; set; }

    // The FK direction that is allowed: tenant-scoped holding a reference INTO the global zone.
    public Guid GuildId { get; set; }

    public Guild Guild { get; set; } = null!;

    // 0-9, the range Blizzard's roster uses. Rank 0 is the guild master in every guild; 1-9 are
    // whatever that guild chose to call them, which is exactly why this table has to exist.
    public int Rank { get; set; }

    public string Name { get; set; } = string.Empty;
}
