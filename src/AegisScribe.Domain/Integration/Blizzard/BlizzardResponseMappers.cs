using AegisScribe.Domain.Managers.Models.Domain;

namespace AegisScribe.Domain.Integration.Blizzard;

// Where Blizzard's wire shapes stop and this app's domain begins. Internal, so the boundary is enforced
// rather than agreed.
//
// One rule runs through all of it: match on the id or the `type` token, never on `name`. Names are
// localized — Blizzard would answer "Krieger" under a de_DE locale — so a switch over them is a bug
// that only appears when someone changes a config value.
internal static class BlizzardResponseMappers
{
    public static Realm ToRealm(this BlizzardRealmResponse response, string region, DateTimeOffset fetchedAt) =>
        response.ToRealm(region, ParseConnectedRealmId(response.ConnectedRealm?.Href), fetchedAt);

    // Every realm in one connected-realm group. This path needs no href parsing at all: the group's id
    // is a field on the document, and it is the same id for every realm inside it.
    public static IReadOnlyList<Realm> ToRealms(
        this BlizzardConnectedRealmResponse response,
        string region,
        DateTimeOffset fetchedAt) =>
        response.Realms is null
            ? []
            : [.. response.Realms.Select(realm => realm.ToRealm(region, response.Id, fetchedAt))];

    // The connected-realm index gives links and nothing else, so the ids come out of the hrefs — the
    // same reading, not following, that ParseConnectedRealmId does.
    public static IReadOnlyList<long> ToConnectedRealmIds(this BlizzardConnectedRealmIndexResponse response) =>
        response.ConnectedRealms is null
            ? []
            : [.. response.ConnectedRealms.Select(link => ParseConnectedRealmId(link.Href))];

    private static Realm ToRealm(
        this BlizzardRealmResponse response,
        string region,
        long connectedRealmId,
        DateTimeOffset fetchedAt)
    {
        var slug = response.Slug
            ?? throw new BlizzardUnavailableException("Blizzard returned a realm with no slug.");

        return new Realm
        {
            Slug = slug,

            // Blizzard's own `region` object names the region in prose ("North America"); the caller's
            // short code is what Realm.Region holds and what every lookup keys on.
            Region = region,
            Name = response.Name ?? slug,

            // The stable per-row source id, and the upsert key for the catalogue (6.4b). Unlike the
            // connected-realm id below, this one identifies exactly this realm, which is what lets a
            // rename update in place.
            BlizzardRealmId = response.Id,
            BlizzardConnectedRealmId = connectedRealmId,
            LastSyncedAt = fetchedAt,
        };
    }

    // The connected-realm id appears nowhere on the realm document as a field — only as the last path
    // segment of connected_realm.href, e.g.
    // https://us.api.blizzard.com/data/wow/connected-realm/1092?namespace=dynamic-us
    //
    // An unparseable href fails the fetch rather than storing 0. A realm row is the anchor every
    // character on it hangs from, and a source id of 0 shared by every realm we could not parse is the
    // kind of quiet corruption that is only discovered much later, by which time it is in every row.
    private static long ParseConnectedRealmId(string? href)
    {
        if (!Uri.TryCreate(href, UriKind.Absolute, out var uri))
        {
            throw new BlizzardUnavailableException(
                "Blizzard returned a realm whose connected-realm link could not be read.");
        }

        var lastSegment = uri.Segments[^1].TrimEnd('/');

        return long.TryParse(lastSegment, out var connectedRealmId)
            ? connectedRealmId
            : throw new BlizzardUnavailableException(
                $"Blizzard returned a connected-realm link ending in '{lastSegment}', which is not an id.");
    }

    public static GuildRosterSnapshot ToRosterSnapshot(
        this BlizzardGuildRosterResponse response,
        DateTimeOffset fetchedAt)
    {
        var guild = response.Guild
            ?? throw new BlizzardUnavailableException("Blizzard returned a guild roster with no guild.");

        var name = guild.Name
            ?? throw new BlizzardUnavailableException("Blizzard returned a guild with no name.");

        var members = response.Members is null
            ? []
            : response.Members.Select(member => ToMembership(member, fetchedAt)).OfType<GuildRosterMembership>().ToList();

        return new GuildRosterSnapshot(
            new Guild
            {
                // RealmId is the caller's to resolve, exactly as for a fetched Character: the gateway
                // has no store and will not invent a realm row.
                Name = name,
                NameLower = name.ToLowerInvariant(),
                Faction = ToFaction(guild.Faction),
                BlizzardGuildId = guild.Id,
                LastSyncedAt = fetchedAt,
            },
            members);
    }

    private static GuildRosterMembership? ToMembership(BlizzardGuildMemberResponse member, DateTimeOffset fetchedAt)
    {
        var character = member.Character;
        var name = character?.Name;
        var realmSlug = character?.Realm?.Slug;

        if (character is null || name is null || realmSlug is null)
        {
            // A member row we cannot place. Dropping the one member is right where failing the whole
            // roster would not be: a 400-member guild should not become unsyncable because one entry
            // arrived malformed.
            return null;
        }

        try
        {
            return new GuildRosterMembership(BuildCharacter(character, name, fetchedAt), realmSlug, member.Rank);
        }
        catch (BlizzardUnavailableException)
        {
            // Class and faction throw when this app cannot represent them — correct for a single
            // character fetch, where failing is the honest answer, but wrong here. A roster is a batch:
            // one member of an unrepresentable class must cost that member, not the other 399. The
            // member simply does not appear until the app grows a value for whatever it was.
            return null;
        }
    }

    private static Character BuildCharacter(
        BlizzardGuildMemberCharacterResponse character, string name, DateTimeOffset fetchedAt) =>
        new()
        {
            Name = name,
            NameLower = name.ToLowerInvariant(),
            Level = character.Level,
            Class = ToCharacterClass(character.PlayableClass),
            Faction = ToFaction(character.Faction),
            BlizzardCharacterId = character.Id,

            // No spec and no item level on this endpoint, so the row is created without them. Gear
            // arrives later from the refresh worker, which already selects characters with no
            // equipment (6.5) — fanning out here would turn one call into eight hundred.
            Spec = null,
            ItemLevel = 0,
            LastSyncedAt = fetchedAt,
        };

    public static Character ToCharacter(this BlizzardCharacterSummaryResponse response, DateTimeOffset fetchedAt)
    {
        var name = response.Name
            ?? throw new BlizzardUnavailableException("Blizzard returned a character with no name.");

        return new Character
        {
            // RealmId and Realm are deliberately left unset. The caller supplied the realm slug and owns
            // resolving it to a local Realm row (6.4); the character summary carries no connected-realm
            // id, so a Realm built here would have to fabricate Realm.BlizzardConnectedRealmId — and a
            // fabricated source id is the one field the erasure routine cannot afford to have wrong.
            Name = name,
            NameLower = name.ToLowerInvariant(),
            Level = response.Level,
            Class = ToCharacterClass(response.CharacterClass),
            Spec = response.ActiveSpec?.Name,
            ItemLevel = response.EquippedItemLevel,
            Faction = ToFaction(response.Faction),
            BlizzardCharacterId = response.Id,

            // Stamped here rather than at the point of persistence: this is the instant the data actually
            // came from Blizzard, and the 30-day refresh obligation is measured against that, not against
            // whenever a later transaction happened to commit.
            LastSyncedAt = fetchedAt,
        };
    }

    public static CharacterEquipment ToCharacterEquipment(
        this BlizzardEquipmentResponse response,
        DateTimeOffset fetchedAt) =>
        new()
        {
            // CharacterId is the caller's to set, for the same reason RealmId is above.
            LastSyncedAt = fetchedAt,
            EquippedItems = response.EquippedItems is null
                ? []
                : [.. response.EquippedItems.Select(ToEquippedItem).OfType<EquippedItem>()],
        };

    private static EquippedItem? ToEquippedItem(BlizzardEquippedItemResponse response)
    {
        // An unmapped slot is a deliberate drop, not a failure. Blizzard reports SHIRT and TABARD, which
        // EquipmentSlot has no member for because nothing in the design reference renders them — they
        // carry no item level and contribute nothing to a gear read.
        if (ToSlot(response.Slot?.Type) is not { } slot)
        {
            return null;
        }

        return new EquippedItem
        {
            Slot = slot,
            BlizzardItemId = response.Item?.Id ?? 0,
            ItemName = response.Name ?? string.Empty,
            Quality = ToQuality(response.Quality?.Type),
            ItemLevel = response.Level?.Value ?? 0,

            // No icon. The equipment response carries only a media *href*; resolving it to a filename is
            // a separate /data/wow/media/item/{id} call per item, and doing that here would turn one
            // character fetch into seventeen. Icon sync has its own dedupe story — the same icon name is
            // shared by thousands of items — and belongs with the item catalogue, not here.
            IconName = null,
        };
    }

    // Class and faction identify the character itself, so an unmappable value fails the whole fetch
    // rather than guessing. The caller treats that as "unavailable" and falls back to the stored row,
    // which is the right answer: we would otherwise persist a character as the wrong class, and
    // CharacterMappers.ClassColorHex would throw further downstream where the cause is invisible.
    // A new class arrives once an expansion; the fix is one enum member.
    private static CharacterClass ToCharacterClass(BlizzardKeyedName? characterClass) => characterClass?.Id switch
    {
        1 => CharacterClass.Warrior,
        2 => CharacterClass.Paladin,
        3 => CharacterClass.Hunter,
        4 => CharacterClass.Rogue,
        5 => CharacterClass.Priest,
        6 => CharacterClass.DeathKnight,
        7 => CharacterClass.Shaman,
        8 => CharacterClass.Mage,
        9 => CharacterClass.Warlock,
        10 => CharacterClass.Monk,
        11 => CharacterClass.Druid,
        12 => CharacterClass.DemonHunter,
        13 => CharacterClass.Evoker,
        _ => throw new BlizzardUnavailableException(
            $"Blizzard returned character class id {characterClass?.Id.ToString() ?? "(none)"}, which this " +
            "application cannot represent."),
    };

    // NEUTRAL is a real value — a Pandaren who has not yet picked a side — and it lands here rather than
    // in CharacterFaction because a neutral character has no roster, no faction colour and nothing this
    // app displays. It fails the fetch for the same reason an unknown class does.
    private static CharacterFaction ToFaction(BlizzardTypedName? faction) => faction?.Type switch
    {
        "ALLIANCE" => CharacterFaction.Alliance,
        "HORDE" => CharacterFaction.Horde,
        _ => throw new BlizzardUnavailableException(
            $"Blizzard returned faction '{faction?.Type ?? "(none)"}', which this application cannot represent."),
    };

    // Unlike class and faction, an unknown quality degrades instead of failing. Quality is decoration on
    // one item; dropping the item — or the whole character — over a colour would lose the name and item
    // level, which are the parts a gear read is actually for.
    private static ItemQuality ToQuality(string? quality) => quality switch
    {
        "POOR" => ItemQuality.Poor,
        "COMMON" => ItemQuality.Common,
        "UNCOMMON" => ItemQuality.Uncommon,
        "RARE" => ItemQuality.Rare,
        "EPIC" => ItemQuality.Epic,
        "LEGENDARY" => ItemQuality.Legendary,
        "ARTIFACT" => ItemQuality.Artifact,
        "HEIRLOOM" => ItemQuality.Heirloom,
        _ => ItemQuality.Common,
    };

    private static EquipmentSlot? ToSlot(string? slot) => slot switch
    {
        "HEAD" => EquipmentSlot.Head,
        "NECK" => EquipmentSlot.Neck,
        "SHOULDER" => EquipmentSlot.Shoulder,
        "BACK" => EquipmentSlot.Back,
        "CHEST" => EquipmentSlot.Chest,
        "WRIST" => EquipmentSlot.Wrist,
        "HANDS" => EquipmentSlot.Hands,
        "WAIST" => EquipmentSlot.Waist,
        "LEGS" => EquipmentSlot.Legs,
        "FEET" => EquipmentSlot.Feet,
        "FINGER_1" => EquipmentSlot.Finger1,
        "FINGER_2" => EquipmentSlot.Finger2,
        "TRINKET_1" => EquipmentSlot.Trinket1,
        "TRINKET_2" => EquipmentSlot.Trinket2,
        "MAIN_HAND" => EquipmentSlot.MainHand,
        "OFF_HAND" => EquipmentSlot.OffHand,
        _ => null,
    };
}
