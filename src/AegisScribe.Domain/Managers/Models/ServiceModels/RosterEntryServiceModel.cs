using AegisScribe.Domain.Managers.Models.Domain;

namespace AegisScribe.Domain.Managers.Models.ServiceModels;

// One roster row: who the character is, and what THIS community calls them.
//
// OfficerNote is populated only when the caller is an officer — null for an ordinary member. A field
// whose value depends on the caller is only safe because this read is NOT cached; if the roster read
// ever gains a cache, this field is the reason it cannot be a plain tenant-keyed one.
public class RosterEntryServiceModel
{
    public Guid Id { get; set; }

    // The global Character this row points at. Kept alongside the roster entry's own Id because the
    // character outlives this community's relationship with it.
    public Guid CharacterId { get; set; }

    public string CharacterName { get; set; } = string.Empty;

    // Region travels with the realm because a realm slug alone does not name a character: "argent-dawn"
    // exists in both US and EU. Together with the name they are the character profile's address.
    public string Region { get; set; } = string.Empty;
    public string RealmSlug { get; set; } = string.Empty;
    public CharacterClass Class { get; set; }

    // Mapped API-side by CharacterMappers.ClassColorHex. It travels on the wire because the frontend
    // is forbidden from owning a class→hex table — a second copy is a second thing to get wrong.
    public string ClassColor { get; set; } = string.Empty;

    public int Level { get; set; }
    public int ItemLevel { get; set; }

    // The in-game rank and what this community calls it. Separate from the community's own rank
    // ladder below; neither derives from the other (tenancy.md).
    //
    // All three are null when the character is in no guild this community follows. BlizzardRankName is
    // null again when nobody has named that number — Blizzard does not expose guild rank names, so the
    // UI shows the bare number rather than presenting our guess as the guild's own.
    public int? BlizzardRank { get; set; }
    public string? BlizzardRankName { get; set; }
    public string? GuildName { get; set; }

    // On the wire because staleness is a visible state, and because the 30-day refresh obligation is
    // measured against it.
    public DateTimeOffset LastSyncedAt { get; set; }

    // All null together when nobody has assigned a rank yet. RankSortOrder is here because the roster
    // groups by rank on screen and because it is the first component of this endpoint's sort key.
    public Guid? RankId { get; set; }
    public string? RankName { get; set; }
    public string? RankColour { get; set; }
    public int? RankSortOrder { get; set; }

    // Null when this entry is a main (or simply unlinked). One level deep by construction, so a client
    // grouping the roster never has to recurse.
    public Guid? MainRosterEntryId { get; set; }

    // Both null when unclaimed. The user id rather than an `isMine` flag: the client already knows who
    // it is, and a per-caller field is a cache hazard for whoever adds a cache here later.
    public string? ClaimedByUserId { get; set; }

    public string? ClaimedByDisplayName { get; set; }

    // Officer-private, and null unless the caller is one. See the note at the top of this file.
    public string? OfficerNote { get; set; }

    public DateTimeOffset JoinedAt { get; set; }
}
