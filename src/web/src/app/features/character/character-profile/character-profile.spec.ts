import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';
import { CharacterService } from '../../../core/character.service';
import { CharacterDetailServiceModel } from '../../../models/character.models';
import { CharacterProfile } from './character-profile';

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
    lastSyncedAt: '2026-09-02T07:41:00Z',
    equipment: [
      { slot: 'MainHand', blizzardItemId: 1, itemName: "Scalecommander's Sundering Edge", quality: 'Artifact', itemLevel: 652, iconUrl: null },
    ],
    classColor: '#33937F',
    isDegraded: false,
    ...overrides,
  };
}

async function createWith(
  getCharacter: () => Observable<CharacterDetailServiceModel>,
): Promise<ComponentFixture<CharacterProfile>> {
  await TestBed.configureTestingModule({
    imports: [CharacterProfile],
    providers: [{ provide: CharacterService, useValue: { getCharacter } }],
  }).compileComponents();

  const fixture = TestBed.createComponent(CharacterProfile);
  fixture.componentRef.setInput('region', 'eu');
  fixture.componentRef.setInput('realmSlug', 'argent-dawn');
  fixture.componentRef.setInput('name', 'thornwake');
  return fixture;
}

describe('CharacterProfile', () => {
  it('shows a shape-matched skeleton while loading', async () => {
    const fixture = await createWith(() => new Observable());
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelectorAll('scribe-skeleton').length).toBeGreaterThan(0);
  });

  it('shows the empty state, naming the character and realm, on a 404', async () => {
    const fixture = await createWith(() => throwError(() => new HttpErrorResponse({ status: 404 })));
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('scribe-empty-state')?.textContent).toContain(
      'No character called "thornwake" on argent-dawn',
    );
  });

  it('shows a retryable error state for a genuine failure, distinct from a 404', async () => {
    const fixture = await createWith(() => throwError(() => new HttpErrorResponse({ status: 500 })));
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('scribe-empty-state')?.textContent).toContain("Couldn't load");
    expect(host.querySelector('scribe-empty-state')?.querySelector('button')).not.toBeNull();
  });

  it('renders the banner, equipment rails and weapon row once loaded, class-coloured from the response', async () => {
    const fixture = await createWith(() => of(character()));
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('scribe-character-banner')).not.toBeNull();
    expect(host.querySelectorAll('scribe-equipment-rail').length).toBe(2);
    expect(host.querySelector('.weapon-row')?.textContent).toContain("Scalecommander's Sundering Edge");
    expect((host.querySelector('.character-profile') as HTMLElement)?.style.getPropertyValue('--class-color')).toBe(
      '#33937F',
    );
  });

  it('the provenance panel is always visible, regardless of the active tab', async () => {
    const fixture = await createWith(() => of(character()));
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('scribe-provenance-panel')).not.toBeNull();

    host.querySelectorAll<HTMLButtonElement>('.tab')[2].click(); // Progression
    fixture.detectChanges();

    expect(host.querySelector('scribe-provenance-panel')).not.toBeNull();
  });

  it('switches tab content: Progression shows the progression panel, Overview shows the rails', async () => {
    const fixture = await createWith(() => of(character()));
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    host.querySelectorAll<HTMLButtonElement>('.tab')[2].click(); // Progression
    fixture.detectChanges();

    expect(host.querySelector('scribe-progression-panel')).not.toBeNull();
    expect(host.querySelector('scribe-equipment-rail')).toBeNull();
  });

  it('Specialisations and Collections show an honest "not built yet" state, not invented content', async () => {
    const fixture = await createWith(() => of(character()));
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    host.querySelectorAll<HTMLButtonElement>('.tab')[1].click(); // Specialisations
    fixture.detectChanges();

    expect(host.querySelector('scribe-empty-state')?.textContent).toContain("aren't built yet");
  });
});
