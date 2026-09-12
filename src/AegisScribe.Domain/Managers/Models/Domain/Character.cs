namespace AegisScribe.Domain.Managers.Models.Domain;

public class Character
{
    public Guid Id { get; set; }
    public Guid RealmId { get; set; }
    public Realm Realm { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string NameLower { get; set; } = null!;
    public int Level { get; set; }
    public CharacterClass Class { get; set; }
    public string? Spec { get; set; }
    public int ItemLevel { get; set; }
    public CharacterFaction Faction { get; set; }
    public long? BlizzardCharacterId { get; set; }
    public DateTimeOffset LastSyncedAt { get; set; }
}
