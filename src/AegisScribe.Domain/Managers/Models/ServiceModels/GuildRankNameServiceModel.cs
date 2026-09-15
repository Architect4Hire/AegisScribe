namespace AegisScribe.Domain.Managers.Models.ServiceModels;

// One in-game rank of one followed guild, as this community names it (7.5).
public class GuildRankNameServiceModel
{
    public Guid GuildId { get; set; }

    public string GuildName { get; set; } = string.Empty;

    // 0-9, the range Blizzard's guild roster uses.
    public int Rank { get; set; }

    // Null when nobody has named this rank yet. The UI shows the bare number then — inventing a name
    // would be presenting our guess as the guild's own, which is the one thing this table exists to
    // avoid doing.
    public string? Name { get; set; }
}
