using AegisScribe.Domain.Managers.Models.Domain;

namespace AegisScribe.Domain.Managers.Models.ServiceModels;

public class GuildServiceModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string RealmSlug { get; set; } = string.Empty;
    public CharacterFaction Faction { get; set; }
    public int MemberCount { get; set; }

    // The in-game roster's own freshness. Shown so an officer can tell "nobody has joined" from
    // "we have not asked Blizzard lately" — and because the 30-day obligation is measured against it.
    public DateTimeOffset LastSyncedAt { get; set; }
}
