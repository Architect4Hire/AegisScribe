using AegisScribe.Domain.Managers.Models.Domain;

namespace AegisScribe.Domain.Managers.Models.ServiceModels;

// One roster row, as design screen S3 draws it: who the character is, and what THIS community calls
// them. The rank fields are flattened rather than nested because the row renders them as a single
// pill, and Colour is tenant config the pill surfaces as --rank-color, not a design token.
//
// OfficerNote IS on this model as of 7.4, but populated only when the caller is an officer — null for
// an ordinary member, who can see the row but not what the officers wrote about it.
//
// A field whose value depends on the caller is only safe because this read is NOT cached. The same
// shape on 7.2b's claim state would have been a real leak: that response is cached under a tenant key,
// so the first caller's view would have been served to every other member. If the roster read ever
// gains a cache, this field is the reason it cannot be a plain tenant-keyed one.
public class RosterEntryServiceModel
{
    public Guid Id { get; set; }

    // The global Character this row points at. Kept alongside the roster entry's own Id because they
    // are different things — the character outlives this community's relationship with it.
    public Guid CharacterId { get; set; }

    public string CharacterName { get; set; } = string.Empty;
    public string RealmSlug { get; set; } = string.Empty;
    public CharacterClass Class { get; set; }

    // The token value for this class, mapped API-side by CharacterMappers.ClassColorHex. It travels on
    // the wire because the frontend is forbidden from owning a class→hex table — the design-system
    // skill is explicit, and a second copy of that map is a second thing to get wrong when Blizzard
    // adds a class.
    public string ClassColor { get; set; } = string.Empty;

    public int Level { get; set; }
    public int ItemLevel { get; set; }

    // The in-game rank, and what this community calls it. BOTH live alongside the community's own
    // rank above, and neither derives from the other (tenancy.md) — a fact the roster renders as two
    // separate columns rather than one combined cell.
    //
    // All three are null when the character is not in any guild this community follows.
    // BlizzardRankName is null again when nobody has named that number yet, and the UI then shows the
    // bare rank: Blizzard does not expose guild rank names, so inventing one would present our guess
    // as the guild's own.
    public int? BlizzardRank { get; set; }
    public string? BlizzardRankName { get; set; }
    public string? GuildName { get; set; }

    // When this character's data was last refreshed from Blizzard. On the wire because staleness is a
    // visible state rather than a hidden one (the design system's four states), and because the
    // 30-day refresh obligation is measured against it.
    public DateTimeOffset LastSyncedAt { get; set; }

    // All null together when nobody has assigned a rank yet — a character can sit on the roster
    // before the community decides what they are.
    //
    // RankSortOrder is here because the roster groups rows by rank on screen, and because it is the
    // first component of this endpoint's sort key: the controller encodes it into the next cursor,
    // the same way the character search encodes the last row's name.
    public Guid? RankId { get; set; }
    public string? RankName { get; set; }
    public string? RankColour { get; set; }
    public int? RankSortOrder { get; set; }

    // Null when this entry is a main (or simply unlinked). One level deep by construction, so a client
    // grouping the roster never has to recurse — an entry is either a main or an alt of one (7.3).
    public Guid? MainRosterEntryId { get; set; }

    // Who has claimed this character in this community, if anyone (7.2b). Both null when unclaimed.
    // The user id rather than an `isMine` flag, for the same reason CharacterClaimServiceModel carries
    // one: the client already knows who it is, and a per-caller field is a cache hazard waiting for
    // whoever adds a cache here later.
    public string? ClaimedByUserId { get; set; }

    public string? ClaimedByDisplayName { get; set; }

    // Officer-private, and null unless the caller is one. See the note at the top of this file.
    public string? OfficerNote { get; set; }

    public DateTimeOffset JoinedAt { get; set; }
}
