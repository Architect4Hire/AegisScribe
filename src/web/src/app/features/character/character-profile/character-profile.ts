import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CharacterService } from '../../../core/character.service';
import { ClaimService } from '../../../core/claim.service';
import { CurrentUserService } from '../../../core/current-user.service';
import { CharacterDetailServiceModel } from '../../../models/character.models';
import { CharacterClaimServiceModel } from '../../../models/roster.models';
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

const LEFT_SLOTS: EquipmentSlot[] = [
  'Head',
  'Neck',
  'Shoulder',
  'Back',
  'Chest',
  'Wrist',
  'Hands',
  'Waist',
];
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
  imports: [
    CharacterBanner,
    EmptyState,
    EquipmentRail,
    ItemCell,
    ProfessionPanel,
    ProgressionPanel,
    ProvenancePanel,
    Skeleton,
  ],
  selector: 'scribe-character-profile',
  styleUrl: './character-profile.css',
  templateUrl: './character-profile.html',
})
export class CharacterProfile {
  private readonly characterService = inject(CharacterService);
  private readonly claimService = inject(ClaimService);
  private readonly currentUser = inject(CurrentUserService);
  // Every subscription below is piped through takeUntilDestroyed(this.destroyRef). These are one-shot
  // HttpClient observables, so this is not about a classic leak — it is about a late callback setting
  // signals on a component the user has already navigated away from (frontend.md).
  private readonly destroyRef = inject(DestroyRef);

  readonly region = input.required<string>();
  readonly realmSlug = input.required<string>();
  readonly name = input.required<string>();

  // OPTIONAL, and that is the whole shape of 7.5b. This component serves two routes: the public
  // front door at /characters/... where there is no community, and /t/:slug/characters/... where
  // there is. Undefined on the first, bound from the parent route segment on the second — one
  // component, no fork.
  readonly tenantSlug = input<string | undefined>(undefined);

  readonly tabs = TABS;
  readonly leftSlots = LEFT_SLOTS;
  readonly rightSlots = RIGHT_SLOTS;

  readonly state = signal<LoadState>('loading');
  readonly character = signal<CharacterDetailServiceModel | null>(null);
  readonly activeTab = signal<TabId>('overview');

  // Null with no community in context, which is what makes the banner show no claim controls at all.
  readonly claim = signal<CharacterClaimServiceModel | null>(null);

  // A failed claim reported inline, never by replacing the page. `state` gates whether the character
  // renders at all, so setting it here would take a perfectly good character page away because a
  // claim button failed.
  readonly claimError = signal<string | null>(null);

  readonly currentUserId = computed(() => this.currentUser.user()?.id ?? null);

  // Cosmetics: the clear endpoint carries TenantOfficer itself. Hiding the control is about not
  // offering what would be refused.
  readonly canClear = computed(() => {
    const membership = this.currentUser
      .user()
      ?.memberships.find((entry) => entry.tenantSlug === this.tenantSlug());

    return membership?.role === 'Officer' || membership?.role === 'Owner';
  });

  readonly emptyHeading = computed(
    () => `No character called "${this.name()}" on ${this.realmSlug()}`,
  );

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

  claimCharacter(): void {
    this.withTenantAndCharacter((tenantSlug, characterId) =>
      this.claimService.claim(tenantSlug, characterId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (claim) => {
          this.claimError.set(null);
          this.claim.set(claim);
        },
        // A 409 means somebody claimed it first. Reported inline, with the character still on screen.
        error: () =>
          this.claimError.set(
            'Could not claim this character. Somebody else may have claimed it first.',
          ),
      }),
    );
  }

  releaseClaim(): void {
    this.withTenantAndCharacter((tenantSlug, characterId) =>
      this.claimService.release(tenantSlug, characterId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.clearLocalClaim(characterId),
        error: () => this.claimError.set('Could not release this claim. Try again.'),
      }),
    );
  }

  clearHolder(): void {
    this.withTenantAndCharacter((tenantSlug, characterId) =>
      this.claimService.clearHolder(tenantSlug, characterId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.clearLocalClaim(characterId),
        error: () => this.claimError.set('Could not clear this claim. Try again.'),
      }),
    );
  }

  private clearLocalClaim(characterId: string): void {
    this.claimError.set(null);
    this.claim.set({
      characterId,
      claimedByUserId: null,
      claimedByDisplayName: null,
      claimedAt: null,
    });
  }

  private withTenantAndCharacter(action: (tenantSlug: string, characterId: string) => void): void {
    const tenantSlug = this.tenantSlug();
    const characterId = this.character()?.id;

    // Unreachable from the UI — the controls only render when both are present — but the guard keeps
    // that a property of this method rather than of the template.
    if (tenantSlug && characterId) {
      action(tenantSlug, characterId);
    }
  }

  private load(region: string, realmSlug: string, name: string): void {
    this.state.set('loading');
    this.claim.set(null);
    this.claimError.set(null);

    this.characterService.getCharacter(region, realmSlug, name)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
      next: (character) => {
        this.character.set(character);
        this.state.set('loaded');
        this.loadClaim(character.id);
      },
      error: (error: unknown) => {
        this.character.set(null);
        this.state.set(
          error instanceof HttpErrorResponse && error.status === 404 ? 'empty' : 'error',
        );
      },
    });
  }

  private loadClaim(characterId: string): void {
    const tenantSlug = this.tenantSlug();

    // No community in context — the public front door. Claim state stays null, and the banner shows
    // neither the pill nor the button.
    if (!tenantSlug) {
      return;
    }

    this.claimService.getClaim(tenantSlug, characterId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
      next: (claim) => this.claim.set(claim),
      // Deliberately silent, and deliberately NOT this.state: a character page that loaded is worth
      // more than the claim strip on it. Leaving claim null simply hides the controls.
      error: () => this.claim.set(null),
    });
  }
}
