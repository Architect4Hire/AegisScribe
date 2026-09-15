namespace AegisScribe.Domain.Managers.Models.Domain;

// What this community calls a guild's in-game ranks (7.5).
//
// It exists because Blizzard does not give us the names. The guild roster endpoint returns the rank
// as a NUMBER, 0-9, and nothing else (references/blizzard-endpoints.md) — guild rank names are not
// exposed by the Game Data API at all. So "rank 3 is Veteran" is something a person types.
//
// TENANT-SCOPED, and the rules decide that rather than taste. The global zone is defined as *public
// Blizzard data*; a name somebody typed fails that test outright, whatever it describes. And an
// officer editing a global row has no policy that could authorize it — tenant membership is the only
// authorization model this app has. Two communities following one guild each keep their own names,
// which is the same split TenantGuild already makes: the guild is a fact about the world, what a
// community writes down about it is not.
//
// Distinct again from TenantRank. Three rank concepts now, and none derives from another:
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
