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
  isDegraded: boolean;
}
