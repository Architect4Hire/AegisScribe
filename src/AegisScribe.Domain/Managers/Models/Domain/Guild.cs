namespace AegisScribe.Domain.Managers.Models.Domain;

public class Guild
{
    public Guid Id { get; set; }
    public Guid RealmId { get; set; }
    public Realm Realm { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string NameLower { get; set; } = null!;
    public CharacterFaction Faction { get; set; }
    public long BlizzardGuildId { get; set; }
    public DateTimeOffset LastSyncedAt { get; set; }
}
