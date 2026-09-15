namespace AegisScribe.Domain.Managers.Models.Domain;

public class Realm
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Region { get; set; } = null!;

    // Blizzard's own id for THIS realm, and the upsert key for the realm catalogue (6.4b). Distinct
    // from BlizzardConnectedRealmId below, which names the group and is shared by every realm in it —
    // so it could never identify a row on its own.
    //
    // Zero means "we do not know it yet": a seeded demo realm is fictional and has no Blizzard id at
    // all, and a realm row can predate the catalogue sync. The unique index is filtered to match.
    public long BlizzardRealmId { get; set; }

    // The connected-realm group. Shared across realms, therefore deliberately not unique.
    public long BlizzardConnectedRealmId { get; set; }
    public DateTimeOffset LastSyncedAt { get; set; }
}
