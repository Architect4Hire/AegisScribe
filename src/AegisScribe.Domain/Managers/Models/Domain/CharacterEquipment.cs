namespace AegisScribe.Domain.Managers.Models.Domain;

public class CharacterEquipment
{
    public Guid Id { get; set; }
    public Guid CharacterId { get; set; }
    public Character Character { get; set; } = null!;

    // Fetched via a separate Blizzard endpoint from the character summary,
    // so it tracks its own staleness independently.
    public DateTimeOffset LastSyncedAt { get; set; }
}
