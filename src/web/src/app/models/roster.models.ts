import { CharacterClass } from './character.models';

// Mirrors `CursorPageServiceModel<T>`. The cursor is opaque — pass it back unexamined
// (api-contract.md); nothing here should ever decode or construct one.
export interface CursorPageServiceModel<T> {
  items: T[];
  nextCursor: string | null;
  hasMore: boolean;
}

// Mirrors `RosterSort`. A string union rather than a lookup the UI depends on being exhaustive: a
// value added after this build shipped must not break us (api-contract.md).
export type RosterSort = 'Rank' | 'Name' | 'ItemLevel';

// Mirrors `RosterEntryServiceModel`.
//
// Three rank concepts live on this row and none derives from another (tenancy.md): `rankName` is the
// community's own ladder, `blizzardRank` the number the game reports, `blizzardRankName` what this
// community calls that number. The table renders the first two in separate columns for that reason.
export interface RosterEntryServiceModel {
  id: string;
  characterId: string;
  characterName: string;
  // With realmSlug and characterName, the character profile's address — a realm slug alone exists in
  // more than one region.
  region: string;
  realmSlug: string;
  // `class` matches the JSON key. A friendlier name would deserialize to undefined and fail silently.
  class: CharacterClass;
  // Mapped API-side. The frontend is forbidden a class→hex table of its own.
  classColor: string;
  level: number;
  itemLevel: number;

  // All three null together when the character is in no guild this community follows.
  blizzardRank: number | null;
  blizzardRankName: string | null;
  guildName: string | null;

  lastSyncedAt: string;

  // Null when nobody has assigned a rank yet.
  rankId: string | null;
  rankName: string | null;
  rankColour: string | null;
  rankSortOrder: number | null;

  // Null when this entry IS a main. One level deep by construction, so grouping never recurses.
  mainRosterEntryId: string | null;

  // Null when unclaimed. The user id rather than an `isMine` flag — the signed-in user is already
  // known from GET /api/v1/me, so "is this mine" is a comparison.
  claimedByUserId: string | null;
  claimedByDisplayName: string | null;

  // Null unless the caller is an officer. Not "sometimes missing" — officer-private by design.
  officerNote: string | null;

  joinedAt: string;
}

// Mirrors `AddRosterEntryViewModel`. A character id, not a realm and name: the character must already
// exist in the global zone, which is what the preceding character lookup guarantees.
export interface AddRosterEntryViewModel {
  characterId: string;
  tenantRankId: string | null;
}

// Mirrors `CharacterClaimServiceModel` — who in THIS community has claimed a character. All three
// nullable fields are null together when nobody has.
//
// No `isMine` flag by design: the response is cached under a tenant key, so a per-caller field would
// be computed for the first reader and then served to every other member.
export interface CharacterClaimServiceModel {
  characterId: string;
  claimedByUserId: string | null;
  claimedByDisplayName: string | null;
  claimedAt: string | null;
}

// Mirrors `TenantRankServiceModel` — the community's own ladder.
export interface TenantRankServiceModel {
  id: string;
  name: string;
  sortOrder: number;
  colour: string;
}

// Mirrors `CreateRankViewModel` / `UpdateRankViewModel`. Two types on the server so they can evolve
// independently; identical today, so one shape here.
export interface RankViewModel {
  name: string;
  sortOrder: number;
  colour: string;
}

// Mirrors `GuildRankNameServiceModel`. `name` is null for a rank nobody has named — the UI shows the
// bare number, because inventing a name would present our guess as the guild's own.
export interface GuildRankNameServiceModel {
  guildId: string;
  guildName: string;
  rank: number;
  name: string | null;
}
