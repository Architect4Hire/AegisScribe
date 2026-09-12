using AegisScribe.Domain.Managers.Models.Domain;

namespace AegisScribe.Domain.Managers.Models.ServiceModels;

public class CharacterSummaryServiceModel
{
    public Guid Id { get; set; }
    public string RealmSlug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Level { get; set; }
    public CharacterClass Class { get; set; }
    public string? Spec { get; set; }
    public int ItemLevel { get; set; }
    public CharacterFaction Faction { get; set; }
    public DateTimeOffset LastSyncedAt { get; set; }
}
