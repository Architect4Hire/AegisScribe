import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CharacterDetailServiceModel } from '../../../models/character.models';
import { CharacterBanner } from './character-banner';

function character(overrides: Partial<CharacterDetailServiceModel> = {}): CharacterDetailServiceModel {
  return {
    id: 'c1',
    realmSlug: 'argent-dawn',
    name: 'Thornwake',
    level: 80,
    class: 'Evoker',
    spec: 'Devastation',
    itemLevel: 639,
    faction: 'Alliance',
    lastSyncedAt: new Date().toISOString(),
    equipment: [],
    classColor: '#33937F',
    isDegraded: false,
    ...overrides,
  };
}

describe('CharacterBanner', () => {
  let fixture: ComponentFixture<CharacterBanner>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CharacterBanner],
    }).compileComponents();

    fixture = TestBed.createComponent(CharacterBanner);
    fixture.componentRef.setInput('region', 'eu');
  });

  it("renders the character's name, level, class and spec", () => {
    fixture.componentRef.setInput('character', character());
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('.char-name')?.textContent).toContain('Thornwake');
    expect(host.textContent).toContain('Level 80 Evoker');
    expect(host.textContent).toContain('Devastation');
  });

  it('title-cases the realm slug and shows the region from the route', () => {
    fixture.componentRef.setInput('character', character({ realmSlug: 'argent-dawn' }));
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Argent Dawn (EU)');
  });

  it('shows a fresh "Synced" pill when not degraded', () => {
    fixture.componentRef.setInput('character', character({ isDegraded: false }));
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('.pill')?.classList.contains('pill-ok')).toBe(true);
    expect(host.querySelector('.pill')?.textContent).toContain('Synced');
  });

  it('shows an attention "Stale" pill when degraded, never a fresh-looking pill over old data', () => {
    fixture.componentRef.setInput(
      'character',
      character({ isDegraded: true, lastSyncedAt: new Date(Date.now() - 6 * 86_400_000).toISOString() }),
    );
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    const pill = host.querySelector('.pill');
    expect(pill?.classList.contains('pill-attention')).toBe(true);
    expect(pill?.textContent).toContain('Stale');
  });

  it('shows the real item level stat tile', () => {
    fixture.componentRef.setInput('character', character({ itemLevel: 639 }));
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('.tile-value')?.textContent).toContain(
      '639',
    );
  });
});

// 7.5b. The claim inputs are optional, and the tenant-less case is the one the restriction is really
// about: on the public front door there is no community to claim a character FOR, so the banner shows
// neither the pill nor the button — not a disabled button, nothing at all.
describe('CharacterBanner claim state', () => {
  let fixture: ComponentFixture<CharacterBanner>;

  function host(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function actionLabels(): string[] {
    return [...host().querySelectorAll('.char-actions button')].map(
      (button) => button.textContent?.trim() ?? '',
    );
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CharacterBanner],
    }).compileComponents();

    fixture = TestBed.createComponent(CharacterBanner);
    fixture.componentRef.setInput('region', 'eu');
    fixture.componentRef.setInput('character', character());
  });

  it('shows no pill and no claim button with no community in context', () => {
    // claim stays at its default of null — the public route never sets it.
    fixture.detectChanges();

    expect(host().textContent).not.toContain('Claimed by');
    expect(actionLabels()).not.toContain('Claim character');
    expect(actionLabels()).not.toContain('Unclaim');
    // And the banner itself still renders — the page works without a tenant.
    expect(host().querySelector('.char-name')?.textContent).toContain('Thornwake');
  });

  it('offers Claim on an unclaimed character inside a community', () => {
    fixture.componentRef.setInput('claim', {
      characterId: 'c1',
      claimedByUserId: null,
      claimedByDisplayName: null,
      claimedAt: null,
    });
    fixture.componentRef.setInput('currentUserId', 'u1');
    fixture.detectChanges();

    expect(actionLabels()).toContain('Claim character');
    expect(host().textContent).not.toContain('Claimed by');
  });

  it('shows "Claimed by you" and offers Unclaim on my own claim', () => {
    fixture.componentRef.setInput('claim', {
      characterId: 'c1',
      claimedByUserId: 'u1',
      claimedByDisplayName: 'Me',
      claimedAt: new Date().toISOString(),
    });
    fixture.componentRef.setInput('currentUserId', 'u1');
    fixture.detectChanges();

    expect(host().textContent).toContain('Claimed by you');
    expect(actionLabels()).toContain('Unclaim');
  });

  it("names the holder but offers a non-officer no action on somebody else's claim", () => {
    fixture.componentRef.setInput('claim', {
      characterId: 'c1',
      claimedByUserId: 'u2',
      claimedByDisplayName: 'Thornbite',
      claimedAt: new Date().toISOString(),
    });
    fixture.componentRef.setInput('currentUserId', 'u1');
    fixture.componentRef.setInput('canClear', false);
    fixture.detectChanges();

    expect(host().textContent).toContain('Claimed by Thornbite');
    expect(actionLabels()).not.toContain('Clear claim');
    expect(actionLabels()).not.toContain('Unclaim');
  });

  it("offers an officer Clear claim on somebody else's claim", () => {
    fixture.componentRef.setInput('claim', {
      characterId: 'c1',
      claimedByUserId: 'u2',
      claimedByDisplayName: 'Thornbite',
      claimedAt: new Date().toISOString(),
    });
    fixture.componentRef.setInput('currentUserId', 'u1');
    fixture.componentRef.setInput('canClear', true);
    fixture.detectChanges();

    // "Clear", not "Take" — the endpoint frees the claim and refuses to reassign it.
    expect(actionLabels()).toContain('Clear claim');
  });

  it('falls back to a neutral label when the holder has no display name', () => {
    fixture.componentRef.setInput('claim', {
      characterId: 'c1',
      claimedByUserId: 'u2',
      claimedByDisplayName: null,
      claimedAt: new Date().toISOString(),
    });
    fixture.componentRef.setInput('currentUserId', 'u1');
    fixture.detectChanges();

    // Never an empty label, and never an email — that is not a fellow member's to see.
    expect(host().textContent).toContain('Claimed by another member');
  });
});
