import { CharacterClass } from './character.models';

// Mirrors `CursorPageServiceModel<T>`. The cursor is opaque — pass it back unexamined
// (api-contract.md); nothing here should ever decode or construct one.
export interface CursorPageServiceModel<T> {
  items: T[];
  nextCursor: string | null;
  hasMore: boolean;
}

// Mirrors `RosterSort`. Enums cross the wire as their NAME, and a value added after this build
// shipped must not break us (api-contract.md) — so this is a string union the caller only ever
// widens, never a lookup the UI depends on being exhaustive.
export type RosterSort = 'Rank' | 'Name' | 'ItemLevel';

// Mirrors `RosterEntryServiceModel`.
//
// THREE rank concepts live on this row and none derives from another (tenancy.md):
//   rankName        — the community's own ladder (TenantRank).
//   blizzardRank    — the number the game reports, 0-9.
//   blizzardRankName— what this community calls that number, because Blizzard does not tell us.
// The table renders the first two in separate columns for exactly that reason.
export interface RosterEntryServiceModel {
  id: string;
  characterId: string;
  characterName: string;
  realmSlug: string;
  // `class` matches the JSON key, the same way CharacterSummaryServiceModel's does. A friendlier
  // name here would deserialize to undefined and fail silently.
  class: CharacterClass;
  // Mapped API-side from the class. The frontend is forbidden a class→hex table of its own — one
  // mapping, in CharacterMappers, so a new class is added in one place.
  classColor: string;
  level: number;
  itemLevel: number;

  // All three null together when the character is in no guild this community follows.
  blizzardRank: number | null;
  blizzardRankName: string | null;
  guildName: string | null;

  lastSyncedAt: string;

  // Null when nobody has assigned a rank yet — a character can sit on the roster before the
  // community decides what they are.
  rankId: string | null;
  rankName: string | null;
  rankColour: string | null;
  rankSortOrder: number | null;

  // Null when this entry IS a main. One level deep by construction, so grouping never recurses.
  mainRosterEntryId: string | null;

  // Null when unclaimed. The user id rather than an `isMine` flag — the signed-in user is already
  // known from GET /api/v1/me, so "is this mine" is a comparison rather than a field the server has
  // to compute per caller.
  claimedByUserId: string | null;
  claimedByDisplayName: string | null;

  // Null unless the caller is an officer. Not "sometimes missing" — officer-private by design.
  officerNote: string | null;

  joinedAt: string;
}

// Mirrors `CharacterClaimServiceModel` — who in THIS community has claimed a character.
//
// All three nullable fields are null together when nobody has. There is no `isMine` flag by design:
// the response is cached under a tenant key, so a per-caller field would have been computed for the
// first reader and then served to every other member. "Is this mine" is a comparison against the
// signed-in user, who the SPA already knows from GET /api/v1/me.
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

// Mirrors `CreateRankViewModel` / `UpdateRankViewModel`. Two separate types on the server so they can
// evolve independently; identical today, so one shape here with a comment rather than a copy.
export interface RankViewModel {
  name: string;
  sortOrder: number;
  colour: string;
}

// Mirrors `GuildRankNameServiceModel`. `name` is null for a rank nobody has named — the UI shows the
// bare number then, because inventing a name would present our guess as the guild's own.
export interface GuildRankNameServiceModel {
  guildId: string;
  guildName: string;
  rank: number;
  name: string | null;
}
