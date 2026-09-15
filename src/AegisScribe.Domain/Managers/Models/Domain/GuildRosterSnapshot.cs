namespace AegisScribe.Domain.Managers.Models.Domain;

// One guild roster as Blizzard just returned it, in domain entities rather than anything
// Blizzard-shaped (external.md). A carrier, not a stored entity: the DataLayer reconciles it.
//
// Members carry full Character entities because the roster contains almost everything one needs. Only
// the spec and item level are missing, and neither is on this endpoint at any price.
public sealed record GuildRosterSnapshot(Guild Guild, IReadOnlyList<GuildRosterMembership> Members);

// A character and what the GAME says their rank is (0-9). Never the community's own TenantRank —
// neither derives from the other (tenancy.md).
public sealed record GuildRosterMembership(Character Character, string RealmSlug, int BlizzardRank);
