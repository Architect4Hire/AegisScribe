import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';
import { CharacterService } from '../../../core/character.service';
import { ClaimService } from '../../../core/claim.service';
import { CurrentUserService } from '../../../core/current-user.service';
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

// 7.5b. The same component serves two routes, and this is the seam between them: with no community in
// context it must not even ASK about claims, and with one it loads claim state and hands it to the
// banner. The tenant-less case is the restriction's actual subject.
describe('CharacterProfile claim state', () => {
  function createWithTenant(
    tenantSlug: string | undefined,
    claimState: Partial<{
      getClaim: ReturnType<typeof vi.fn>;
      claim: ReturnType<typeof vi.fn>;
      release: ReturnType<typeof vi.fn>;
      clearHolder: ReturnType<typeof vi.fn>;
    }> = {},
    role: 'Member' | 'Officer' = 'Member',
  ) {
    const claims = {
      getClaim:
        claimState.getClaim ??
        vi.fn(() =>
          of({ characterId: 'c1', claimedByUserId: null, claimedByDisplayName: null, claimedAt: null }),
        ),
      claim:
        claimState.claim ??
        vi.fn(() =>
          of({
            characterId: 'c1',
            claimedByUserId: 'u1',
            claimedByDisplayName: 'Me',
            claimedAt: new Date().toISOString(),
          }),
        ),
      release: claimState.release ?? vi.fn(() => of(void 0)),
      clearHolder: claimState.clearHolder ?? vi.fn(() => of(void 0)),
    };

    TestBed.configureTestingModule({
      imports: [CharacterProfile],
      providers: [
        { provide: CharacterService, useValue: { getCharacter: () => of(character()) } },
        { provide: ClaimService, useValue: claims },
        {
          provide: CurrentUserService,
          useValue: {
            user: () => ({
              id: 'u1',
              memberships: [
                { tenantId: 't', tenantSlug: 'emberfall', tenantName: 'Emberfall', role, joinedAt: '' },
              ],
            }),
          },
        },
      ],
    });

    const fixture = TestBed.createComponent(CharacterProfile);
    fixture.componentRef.setInput('region', 'eu');
    fixture.componentRef.setInput('realmSlug', 'argent-dawn');
    fixture.componentRef.setInput('name', 'thornwake');
    if (tenantSlug !== undefined) {
      fixture.componentRef.setInput('tenantSlug', tenantSlug);
    }
    fixture.detectChanges();

    return { fixture, claims, host: fixture.nativeElement as HTMLElement };
  }

  it('never asks about claims with no community in context', () => {
    const { claims, host } = createWithTenant(undefined);

    // Not merely hidden — never requested. This route has to work for a visitor with no session, and
    // the claims endpoint requires membership.
    expect(claims.getClaim).not.toHaveBeenCalled();
    expect(host.textContent).not.toContain('Claim character');
    // The page itself still renders.
    expect(host.querySelector('.char-name')?.textContent).toContain('Thornwake');
  });

  it('loads claim state and offers Claim inside a community', () => {
    const { claims, host } = createWithTenant('emberfall');

    expect(claims.getClaim).toHaveBeenCalledWith('emberfall', 'c1');
    expect(host.textContent).toContain('Claim character');
  });

  it('keeps the character page when the claim lookup fails', () => {
    // A character page that loaded is worth more than the claim strip on it.
    const { host } = createWithTenant('emberfall', {
      getClaim: vi.fn(() => throwError(() => new Error('offline'))),
    });

    expect(host.querySelector('.char-name')?.textContent).toContain('Thornwake');
    expect(host.textContent).not.toContain("Couldn't load this character");
  });

  it('keeps the character page when the claim itself fails, reporting inline', () => {
    const { host, fixture } = createWithTenant('emberfall', {
      claim: vi.fn(() => throwError(() => new Error('conflict'))),
    });

    fixture.componentInstance.claimCharacter();
    fixture.detectChanges();

    expect(host.querySelector('.char-name')?.textContent).toContain('Thornwake');
    expect(host.querySelector('.claim-error')?.textContent).toContain('Somebody else may have');
  });

  it('shows the pill and Unclaim once claimed', () => {
    const { host, fixture } = createWithTenant('emberfall');

    fixture.componentInstance.claimCharacter();
    fixture.detectChanges();

    expect(host.textContent).toContain('Claimed by you');
    expect(host.textContent).toContain('Unclaim');
  });
});
