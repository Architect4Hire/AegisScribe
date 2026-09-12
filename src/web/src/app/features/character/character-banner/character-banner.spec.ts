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
