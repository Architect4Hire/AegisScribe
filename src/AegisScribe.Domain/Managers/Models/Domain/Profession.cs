namespace AegisScribe.Domain.Managers.Models.Domain;

public class Profession
{
    public Guid Id { get; set; }
    public long BlizzardProfessionId { get; set; }
    public string Name { get; set; } = null!;
    public DateTimeOffset LastSyncedAt { get; set; }
}
