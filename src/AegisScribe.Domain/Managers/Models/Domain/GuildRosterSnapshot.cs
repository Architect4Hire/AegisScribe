namespace AegisScribe.Domain.Managers.Models.Domain;

// One guild roster as Blizzard just returned it, expressed entirely in domain entities.
//
// The gateway returns this rather than anything Blizzard-shaped (external.md). It is a carrier, not a
// stored entity: nothing persists a snapshot, the DataLayer reconciles it against what is stored.
//
// The members carry full Character entities because the roster genuinely contains almost everything a
// Character needs — name, Blizzard id, realm, level, class, race, faction. Only the spec and the item
// level are missing, and neither is on this endpoint at any price.
public sealed record GuildRosterSnapshot(Guild Guild, IReadOnlyList<GuildRosterMembership> Members);

// A character and what the GAME says their rank is (0-9). Never the community's own TenantRank —
// neither derives from the other (tenancy.md).
public sealed record GuildRosterMembership(Character Character, string RealmSlug, int BlizzardRank);
