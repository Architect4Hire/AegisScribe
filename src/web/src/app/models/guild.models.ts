import { CharacterFaction } from './character.models';

// Mirrors `GuildServiceModel`. A guild this community follows. The Guild itself is GLOBAL reference
// data (tenancy.md) — two communities following the same guild see the same row — and what is private
// is only which guilds a community follows.
export interface GuildServiceModel {
  id: string;
  name: string;
  realmSlug: string;
  faction: CharacterFaction;
  memberCount: number;
  // The in-game roster's own freshness, and what the thirty-day refresh obligation is measured
  // against. Shown so an officer can tell "nobody has joined" from "we have not asked lately".
  lastSyncedAt: string;
}

// Mirrors `LinkGuildViewModel`. No tenant id — it comes from the route (tenancy.md).
export interface LinkGuildViewModel {
  region: string;
  realmSlug: string;
  // The display name as the game shows it, not a slug: slugging it is the server's job.
  guildName: string;
}

// Mirrors `ImportGuildRosterViewModel`.
export interface ImportGuildRosterViewModel {
  guildId: string;
}

// Mirrors `UnaffiliatedRosterEntryServiceModel`.
export interface UnaffiliatedRosterEntryServiceModel {
  rosterEntryId: string;
  characterName: string;
  realmSlug: string;
}

// Mirrors `RosterImportServiceModel`. Import is additive only: it never removes a roster row, so
// characters in none of the linked guilds are REPORTED here rather than acted on.
export interface RosterImportServiceModel {
  imported: number;
  alreadyOnRoster: number;
  // Capped server-side; the count below is the full figure.
  notInAnyLinkedGuild: UnaffiliatedRosterEntryServiceModel[];
  notInAnyLinkedGuildCount: number;
}
