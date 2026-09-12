namespace AegisScribe.Domain.Managers.Models.Domain;

public class Realm
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Region { get; set; } = null!;
    public long BlizzardConnectedRealmId { get; set; }
    public DateTimeOffset LastSyncedAt { get; set; }
}
