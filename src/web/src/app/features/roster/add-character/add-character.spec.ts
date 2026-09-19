import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';
import { CharacterService } from '../../../core/character.service';
import { RosterService } from '../../../core/roster.service';
import { CharacterDetailServiceModel } from '../../../models/character.models';
import { AddCharacter } from './add-character';

const THORNWAKE = {
  id: 'c1',
  realmSlug: 'argent-dawn',
  name: 'Thornwake',
} as CharacterDetailServiceModel;

interface Harness {
  fixture: ComponentFixture<AddCharacter>;
  component: AddCharacter;
  characters: { getCharacter: ReturnType<typeof vi.fn> };
  roster: { addEntry: ReturnType<typeof vi.fn> };
  host: HTMLElement;
}

async function createWith(
  options: {
    getCharacter?: () => Observable<CharacterDetailServiceModel>;
    addEntry?: () => Observable<void>;
  } = {},
): Promise<Harness> {
  const characters = { getCharacter: vi.fn(options.getCharacter ?? (() => of(THORNWAKE))) };
  const roster = { addEntry: vi.fn(options.addEntry ?? (() => of(void 0))) };

  await TestBed.configureTestingModule({
    imports: [AddCharacter],
    providers: [
      { provide: CharacterService, useValue: characters },
      { provide: RosterService, useValue: roster },
    ],
  }).compileComponents();

  const fixture = TestBed.createComponent(AddCharacter);
  fixture.componentRef.setInput('tenantSlug', 'emberfall');
  fixture.detectChanges();

  return {
    fixture,
    component: fixture.componentInstance,
    characters,
    roster,
    host: fixture.nativeElement as HTMLElement,
  };
}

function fill(component: AddCharacter): void {
  component.realm.set('Argent Dawn');
  component.name.set(' Thornwake ');
}

describe('AddCharacter', () => {
  it('looks the character up first, then adds it by id — unranked and unclaimed', async () => {
    const { fixture, component, characters, roster, host } = await createWith();
    const added = vi.fn();
    component.added.subscribe(added);

    fill(component);
    component.add();
    fixture.detectChanges();

    expect(characters.getCharacter).toHaveBeenCalledWith('us', 'argent-dawn', 'Thornwake');
    expect(roster.addEntry).toHaveBeenCalledWith('emberfall', {
      characterId: 'c1',
      tenantRankId: null,
    });
    expect(added).toHaveBeenCalledWith('Thornwake');
    expect(host.textContent).toContain('Added Thornwake to the roster.');
    expect(component.name()).toBe('');
    expect(component.realm()).toBe('Argent Dawn');
  });

  it('says no such character when the lookup finds nobody, and never tries the add', async () => {
    const { fixture, component, roster, host } = await createWith({
      getCharacter: () => throwError(() => new HttpErrorResponse({ status: 404 })),
    });

    fill(component);
    component.add();
    fixture.detectChanges();

    expect(roster.addEntry).not.toHaveBeenCalled();
    expect(host.querySelector('[role="alert"]')?.textContent).toContain(
      'No character called Thornwake on argent-dawn (US)',
    );
    expect(component.name()).toBe(' Thornwake ');
  });

  it('says so when the character is already on the roster', async () => {
    const { fixture, component, host } = await createWith({
      addEntry: () => throwError(() => new HttpErrorResponse({ status: 409 })),
    });

    fill(component);
    component.add();
    fixture.detectChanges();

    expect(host.querySelector('[role="alert"]')?.textContent).toContain(
      'Thornwake is already on the roster.',
    );
  });

  it('opens itself on an empty roster and stays open once it has been opened', async () => {
    const { fixture, component, host } = await createWith();
    const panel = host.querySelector('details') as HTMLDetailsElement;

    expect(panel.open).toBe(false);

    fixture.componentRef.setInput('startOpen', true);
    fixture.detectChanges();
    expect(component.expanded()).toBe(true);

    // The roster reloads after an add and passes through a non-empty state; the panel stays put.
    fixture.componentRef.setInput('startOpen', false);
    fixture.detectChanges();
    expect(component.expanded()).toBe(true);
  });
});
