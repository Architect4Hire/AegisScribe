import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { CharacterService } from '../../../core/character.service';
import { CharacterDetailServiceModel } from '../../../models/character.models';
import { EquipmentSlot, EquippedItemServiceModel } from '../../../models/item.models';
import { EmptyState } from '../../../shared/empty-state/empty-state';
import { EquipmentRail } from '../../../shared/equipment-rail/equipment-rail';
import { ItemCell } from '../../../shared/item-cell/item-cell';
import { Skeleton } from '../../../shared/skeleton/skeleton';
import { CharacterBanner } from '../character-banner/character-banner';
import { ProfessionPanel } from '../profession-panel/profession-panel';
import { ProgressionPanel } from '../progression-panel/progression-panel';
import { ProvenancePanel } from '../provenance-panel/provenance-panel';

type LoadState = 'loading' | 'empty' | 'error' | 'loaded';
type TabId = 'overview' | 'specialisations' | 'progression' | 'professions' | 'collections';

const LEFT_SLOTS: EquipmentSlot[] = ['Head', 'Neck', 'Shoulder', 'Back', 'Chest', 'Wrist', 'Hands', 'Waist'];
const RIGHT_SLOTS: EquipmentSlot[] = ['Legs', 'Feet', 'Finger1', 'Finger2', 'Trinket1', 'Trinket2'];

const TABS: { id: TabId; label: string }[] = [
  { id: 'overview', label: 'Overview' },
  { id: 'specialisations', label: 'Specialisations' },
  { id: 'progression', label: 'Progression' },
  { id: 'professions', label: 'Professions' },
  { id: 'collections', label: 'Collections' },
];

// The routed character-profile screen (design/aegisscribe-armory.html §S1). region/realmSlug/name
// come from the route (withComponentInputBinding), never a stored variable.
@Component({
  imports: [CharacterBanner, EmptyState, EquipmentRail, ItemCell, ProfessionPanel, ProgressionPanel, ProvenancePanel, Skeleton],
  selector: 'scribe-character-profile',
  styleUrl: './character-profile.css',
  templateUrl: './character-profile.html',
})
export class CharacterProfile {
  private readonly characterService = inject(CharacterService);

  readonly region = input.required<string>();
  readonly realmSlug = input.required<string>();
  readonly name = input.required<string>();

  readonly tabs = TABS;
  readonly leftSlots = LEFT_SLOTS;
  readonly rightSlots = RIGHT_SLOTS;

  readonly state = signal<LoadState>('loading');
  readonly character = signal<CharacterDetailServiceModel | null>(null);
  readonly activeTab = signal<TabId>('overview');

  readonly emptyHeading = computed(() => `No character called "${this.name()}" on ${this.realmSlug()}`);

  readonly mainHand = computed<EquippedItemServiceModel | null>(
    () => this.character()?.equipment.find((item) => item.slot === 'MainHand') ?? null,
  );
  readonly offHand = computed<EquippedItemServiceModel | null>(
    () => this.character()?.equipment.find((item) => item.slot === 'OffHand') ?? null,
  );

  constructor() {
    effect(() => {
      this.load(this.region(), this.realmSlug(), this.name());
    });
  }

  selectTab(tab: TabId): void {
    this.activeTab.set(tab);
  }

  retry(): void {
    this.load(this.region(), this.realmSlug(), this.name());
  }

  private load(region: string, realmSlug: string, name: string): void {
    this.state.set('loading');
    this.characterService.getCharacter(region, realmSlug, name).subscribe({
      next: (character) => {
        this.character.set(character);
        this.state.set('loaded');
      },
      error: (error: unknown) => {
        this.character.set(null);
        this.state.set(error instanceof HttpErrorResponse && error.status === 404 ? 'empty' : 'error');
      },
    });
  }
}
