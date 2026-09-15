namespace AegisScribe.Domain.Managers.Models.Domain;

public class Realm
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Region { get; set; } = null!;

    // Blizzard's id for THIS realm, and the catalogue's upsert key. Distinct from
    // BlizzardConnectedRealmId below, which names the group and could never identify a row on its own.
    //
    // Zero means "not known yet" — a seeded demo realm is fictional, and a row can predate the
    // catalogue sync. The unique index is filtered to match.
    public long BlizzardRealmId { get; set; }

    // The connected-realm group. Shared across realms, therefore deliberately not unique.
    public long BlizzardConnectedRealmId { get; set; }
    public DateTimeOffset LastSyncedAt { get; set; }
}
