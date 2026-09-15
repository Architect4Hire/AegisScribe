namespace AegisScribe.Domain.Integration.Blizzard;

// Blizzard's wire shapes, and the whole of this repo's knowledge of them. Every type in this file is
// internal on purpose: external.md's "returns domain entities" is only true if there is no way for one
// of these to escape the folder, and `internal` is the only form of that rule a compiler enforces.
//
// Modelled from references/blizzard-endpoints.md -> "Response shapes": responses are HAL-ish, so nested
// objects carry a `key.href` alongside an `id` and a localized `name`. We model the id and the name and
// ignore the hrefs — following them at request time is how one character page becomes forty API calls.
//
// Everything here is nullable or defaulted, because "fields are more optional than they look": a
// character can have no guild, no active spec, and no equipped item in a slot.

// `{ "key": { "href": ... }, "name": "Warrior", "id": 1 }` — a reference to another Game Data document.
// The id is the stable half; the name is localized and must never be matched on.
internal sealed record BlizzardKeyedName
{
    public long Id { get; init; }

    public string? Name { get; init; }
}

// `{ "type": "ALLIANCE", "name": "Alliance" }` — an enumerated value. `type` is the invariant token and
// the only half safe to switch on.
internal sealed record BlizzardTypedName
{
    public string? Type { get; init; }

    public string? Name { get; init; }
}

internal sealed record BlizzardRealmReference
{
    public long Id { get; init; }

    public string? Name { get; init; }

    public string? Slug { get; init; }
}

// GET /data/wow/realm/{realmSlug} — Game Data, dynamic namespace. Realms move between connected-realm
// groups, which is exactly why they are dynamic- rather than static- data.
internal sealed record BlizzardRealmResponse
{
    public long Id { get; init; }

    public string? Name { get; init; }

    public string? Slug { get; init; }

    // The one place this integration reads an href rather than ignoring it. The connected-realm id is
    // not given as a field anywhere on this document — only as the last path segment of this link — and
    // it is the id a realm is actually identified by once realms are grouped. Parsing it is not the same
    // as *following* it: no second request is made. See BlizzardResponseMappers.
    public BlizzardLinkResponse? ConnectedRealm { get; init; }
}

internal sealed record BlizzardLinkResponse
{
    public string? Href { get; init; }
}

// GET /data/wow/connected-realm/index — hrefs and nothing else, so the ids have to be read out of
// them the same way the single-realm document's connected_realm link is read.
internal sealed record BlizzardConnectedRealmIndexResponse
{
    public IReadOnlyList<BlizzardLinkResponse>? ConnectedRealms { get; init; }
}

// GET /data/wow/connected-realm/{connectedRealmId} — the catalogue's whole reason for going this way
// round. One document carries its own id AND every realm in the group, so ~100 of these fill the realm
// table for a region; /data/wow/realm/index is one call but omits connected_realm, costing a follow-up
// per realm to fill the same columns.
internal sealed record BlizzardConnectedRealmResponse
{
    // The connected-realm id, as a field rather than a link — no parsing needed here, unlike the index
    // above and unlike the single-realm document.
    public long Id { get; init; }

    public IReadOnlyList<BlizzardRealmResponse>? Realms { get; init; }
}

// GET /data/wow/guild/{realmSlug}/{nameSlug}/roster
//
// The one Blizzard endpoint that genuinely batches: every member in a single call, AND the guild
// itself alongside them, so linking a guild costs one call rather than two.
//
// The namespace trap applies — this path sits under /data/wow/ and takes profile-{region}.
//
// What it does NOT carry is item level or equipment, at any price. That asymmetry is the whole design
// constraint: membership is cheap, gear is not.
internal sealed record BlizzardGuildRosterResponse
{
    public BlizzardGuildResponse? Guild { get; init; }

    public IReadOnlyList<BlizzardGuildMemberResponse>? Members { get; init; }
}

internal sealed record BlizzardGuildResponse
{
    public long Id { get; init; }

    public string? Name { get; init; }

    public BlizzardTypedName? Faction { get; init; }

    public BlizzardRealmReference? Realm { get; init; }
}

internal sealed record BlizzardGuildMemberResponse
{
    public BlizzardGuildMemberCharacterResponse? Character { get; init; }

    // What the game says: 0 is the guild master, 9 the lowest rank.
    public int Rank { get; init; }
}

// A cut-down character: enough to create a Character row, which is what makes a roster sync possible
// without a call per member. No spec and no item level — see the note on the roster response.
internal sealed record BlizzardGuildMemberCharacterResponse
{
    public long Id { get; init; }

    public string? Name { get; init; }

    public int Level { get; init; }

    public BlizzardTypedName? Faction { get; init; }

    public BlizzardKeyedName? PlayableClass { get; init; }

    public BlizzardRealmReference? Realm { get; init; }
}

// GET /profile/wow/character/{realmSlug}/{characterName}
internal sealed record BlizzardCharacterSummaryResponse
{
    public long Id { get; init; }

    public string? Name { get; init; }

    public int Level { get; init; }

    // Blizzard reports two item levels. `average_item_level` counts what is in the character's bags as
    // well; `equipped_item_level` is the number an armory shows and the one a raid leader means.
    public int EquippedItemLevel { get; init; }

    public int AverageItemLevel { get; init; }

    public BlizzardTypedName? Faction { get; init; }

    public BlizzardKeyedName? CharacterClass { get; init; }

    // Absent on a character that has never chosen a specialization.
    public BlizzardKeyedName? ActiveSpec { get; init; }

    public BlizzardRealmReference? Realm { get; init; }
}

// GET /profile/wow/character/{realmSlug}/{characterName}/equipment
internal sealed record BlizzardEquipmentResponse
{
    public IReadOnlyList<BlizzardEquippedItemResponse>? EquippedItems { get; init; }
}

internal sealed record BlizzardEquippedItemResponse
{
    public BlizzardItemReference? Item { get; init; }

    public BlizzardTypedName? Slot { get; init; }

    public BlizzardTypedName? Quality { get; init; }

    public string? Name { get; init; }

    public BlizzardItemLevelResponse? Level { get; init; }
}

internal sealed record BlizzardItemReference
{
    public long Id { get; init; }
}

// `{ "value": 639, "display_string": "Item Level 639" }` — the display string is localized, the value
// is not.
internal sealed record BlizzardItemLevelResponse
{
    public int Value { get; init; }
}
