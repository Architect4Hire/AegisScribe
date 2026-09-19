using AegisScribe.Domain.Managers.Models.Domain;

namespace AegisScribe.Domain.Integration.Blizzard;

// The only seam in the solution that knows Blizzard exists, and one interface rather than a family:
// splitting Game Data from Profile would follow Blizzard's URL shapes instead of anything this app
// cares about, and every consumer would inject both (external.md).
//
// Consumed only by the DataLayer and the sync worker. Every method returns DOMAIN ENTITIES — Blizzard's
// response types are internal to this folder and have no way out of it.
public interface IBlizzardGateway
{
    // Whether credentials are configured at all. Cheap, does no I/O, and says nothing about whether
    // those credentials are valid — a cache-first read uses it to skip a round trip it knows will fail.
    bool IsConfigured { get; }

    // Proves the whole chain end to end: a token from the regional OAuth host, the bearer header, the
    // regional API host, and the mandatory namespace and locale parameters. Cached, so polling it does
    // not spend the hourly call budget.
    Task<BlizzardAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken);

    // A single realm, by slug; null on 404 like the character reads below. Realms are the anchor every
    // character hangs from — Character.RealmId is not nullable — so a lookup on an unknown realm has to
    // resolve one first. Dynamic namespace, not static: realms move between connected-realm groups.
    Task<Realm?> FetchRealmAsync(string region, string realmSlug, CancellationToken cancellationToken);

    // Every connected-realm id in a region. The realm catalogue walks these rather than
    // /data/wow/realm/index: that one is a single call but omits connected_realm, so filling the columns
    // this app stores would cost a follow-up per realm instead of per group.
    Task<IReadOnlyList<long>> FetchConnectedRealmIdsAsync(string region, CancellationToken cancellationToken);

    // Every realm in one connected-realm group, each already carrying the group id. Empty when Blizzard
    // 404s — a group can be dissolved between the index call and this one, which is a normal answer.
    Task<IReadOnlyList<Realm>> FetchConnectedRealmAsync(
        string region,
        long connectedRealmId,
        CancellationToken cancellationToken);

    // A guild and its entire roster in ONE call — the only Blizzard endpoint that genuinely batches.
    // Null on 404.
    //
    // The snapshot carries no item level and no equipment, because the endpoint does not. Resist the
    // obvious follow-up: fetching gear per member turns one call into twice the roster size, which is
    // what the per-tenant budget exists to prevent. The refresh worker fills those in on its own
    // schedule.
    Task<GuildRosterSnapshot?> FetchGuildRosterAsync(
        string realmSlug,
        string guildNameSlug,
        CancellationToken cancellationToken);

    // Null when Blizzard says 404 — a character that does not exist is a normal answer — and throws
    // BlizzardUnavailableException for anything else, so a caller can tell "no such character" from "we
    // could not ask".
    //
    // The returned Character has no RealmId and no Equipment: the caller supplied the realm slug and
    // owns resolving it, and equipment is a second endpoint.
    Task<Character?> FetchCharacterAsync(string realmSlug, string characterName, CancellationToken cancellationToken);

    // On its own endpoint and therefore with its own LastSyncedAt. Same null-on-404 contract. The
    // returned CharacterEquipment has no CharacterId — the caller sets it when attaching the snapshot.
    Task<CharacterEquipment?> FetchEquipmentAsync(string realmSlug, string characterName, CancellationToken cancellationToken);

    // The avatar and full-body render. Null on 404, which callers record as "no renders" rather than
    // "unknown" — a character can genuinely have none. Throws BlizzardUnavailableException like the
    // reads above, and callers treat that as "unknown" and keep whatever they already held.
    Task<CharacterMedia?> FetchCharacterMediaAsync(string realmSlug, string characterName, CancellationToken cancellationToken);

    // One item's icon from /data/wow/media/item/{id}, in the static namespace. Never called per
    // character: the sync worker asks once per distinct item id and every equipped row shares the
    // answer.
    Task<ItemIconLookup> FetchItemIconAsync(long blizzardItemId, CancellationToken cancellationToken);
}
