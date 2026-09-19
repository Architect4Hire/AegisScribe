import { EquippedItemServiceModel } from './item.models';

// Mirrors `AegisScribe.Domain.Managers.Models.Domain.CharacterClass`.
export type CharacterClass =
  | 'Warrior'
  | 'Paladin'
  | 'Hunter'
  | 'Rogue'
  | 'Priest'
  | 'DeathKnight'
  | 'Shaman'
  | 'Mage'
  | 'Warlock'
  | 'Monk'
  | 'Druid'
  | 'DemonHunter'
  | 'Evoker';

// Mirrors `AegisScribe.Domain.Managers.Models.Domain.CharacterFaction`.
export type CharacterFaction = 'Alliance' | 'Horde';

// Mirrors `CharacterSummaryServiceModel`.
export interface CharacterSummaryServiceModel {
  id: string;
  realmSlug: string;
  name: string;
  level: number;
  class: CharacterClass;
  spec: string | null;
  itemLevel: number;
  faction: CharacterFaction;
  lastSyncedAt: string;
}

// Mirrors `CharacterDetailServiceModel`.
export interface CharacterDetailServiceModel extends CharacterSummaryServiceModel {
  equipment: EquippedItemServiceModel[];
  classColor: string;
  // Blizzard render URLs, referenced directly — never proxied. Null when Blizzard has no renders for
  // this character or has not been asked yet; the banner and centre column fall back to designed
  // states rather than broken images.
  avatarUrl: string | null;
  renderUrl: string | null;
  isDegraded: boolean;
}
