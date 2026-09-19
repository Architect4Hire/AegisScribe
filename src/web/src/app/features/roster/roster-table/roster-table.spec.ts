import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';
import { ClaimService } from '../../../core/claim.service';
import { CurrentUserService } from '../../../core/current-user.service';
import { RosterService } from '../../../core/roster.service';
import {
  CursorPageServiceModel,
  RosterEntryServiceModel,
  TenantRankServiceModel,
} from '../../../models/roster.models';
import { RosterTable } from './roster-table';

const RAIDER: TenantRankServiceModel = {
  id: 'rank-raider',
  name: 'Raider',
  sortOrder: 10,
  colour: '#cba76a',
};

function entry(overrides: Partial<RosterEntryServiceModel> = {}): RosterEntryServiceModel {
  return {
    id: 'entry-1',
    characterId: 'char-1',
    characterName: 'Thornwake',
    region: 'us',
    realmSlug: 'emberfall',
    class: 'Warrior',
    classColor: '#C69B6D',
    level: 80,
    itemLevel: 639,
    blizzardRank: 3,
    blizzardRankName: 'Veteran',
    guildName: 'Emberfall',
    lastSyncedAt: new Date().toISOString(),
    rankId: RAIDER.id,
    rankName: RAIDER.name,
    rankColour: RAIDER.colour,
    rankSortOrder: RAIDER.sortOrder,
    mainRosterEntryId: null,
    claimedByUserId: null,
    claimedByDisplayName: null,
    officerNote: null,
    joinedAt: new Date().toISOString(),
    ...overrides,
  };
}

interface Harness {
  fixture: ComponentFixture<RosterTable>;
  roster: {
    listRoster: ReturnType<typeof vi.fn>;
    listRanks: ReturnType<typeof vi.fn>;
    setRank: ReturnType<typeof vi.fn>;
    setOfficerNote: ReturnType<typeof vi.fn>;
  };
  host: HTMLElement;
}

async function createWith(
  listRoster: () => Observable<CursorPageServiceModel<RosterEntryServiceModel>>,
  role: 'Member' | 'Officer' | null = 'Member',
): Promise<Harness> {
  const roster = {
    listRoster: vi.fn(listRoster),
    listRanks: vi.fn(() => of([RAIDER])),
    setRank: vi.fn(() => of(void 0)),
    setOfficerNote: vi.fn(() => of(void 0)),
  };

  const currentUser = {
    user: () =>
      role === null
        ? null
        : {
            memberships: [
              {
                tenantId: 't',
                tenantSlug: 'emberfall',
                tenantName: 'Emberfall',
                role,
                joinedAt: '',
              },
            ],
          },
  };

  await TestBed.configureTestingModule({
    imports: [RosterTable],
    providers: [
      provideRouter([]),
      { provide: RosterService, useValue: roster },
      { provide: CurrentUserService, useValue: currentUser },
    ],
  }).compileComponents();

  const fixture = TestBed.createComponent(RosterTable);
  fixture.componentRef.setInput('tenantSlug', 'emberfall');
  fixture.detectChanges();

  return { fixture, roster, host: fixture.nativeElement as HTMLElement };
}

function page(rows: RosterEntryServiceModel[]): CursorPageServiceModel<RosterEntryServiceModel> {
  return { items: rows, nextCursor: null, hasMore: false };
}

describe('RosterTable', () => {
  it("links each character to its profile inside this community, in the character's own region", async () => {
    // EU on purpose: a link that assumed US would still pass with the fixture's default.
    const { host } = await createWith(() =>
      of(page([entry({ region: 'eu', realmSlug: 'argent-dawn', characterName: 'Thornwake' })])),
    );

    const link = host.querySelector<HTMLAnchorElement>('a.char-name');
    expect(link?.textContent?.trim()).toBe('Thornwake');
    expect(link?.getAttribute('href')).toBe('/t/emberfall/characters/eu/argent-dawn/Thornwake');
  });

  it('shows BOTH ranks in separate columns, rendered differently', async () => {
    // 7.5's central restriction. The community's rank is a pill; the game's is plain mono text. The
    // difference is deliberate — matching treatments would imply one derives from the other.
    const { host } = await createWith(() => of(page([entry()])));

    expect(host.querySelector('.rank')?.textContent).toContain('Raider');
    expect(host.querySelector('.rank-blizz')?.textContent).toContain('Veteran');
    // And the game's rank is emphatically not a pill.
    expect(host.querySelector('.rank-blizz .rank')).toBeNull();
  });

  it('shows the bare rank number when nobody has named that in-game rank', async () => {
    // Blizzard does not expose guild rank names, so an unnamed rank stays a number rather than being
    // given a label we invented.
    const { host } = await createWith(() =>
      of(page([entry({ blizzardRankName: null, blizzardRank: 5 })])),
    );

    expect(host.querySelector('.rank-blizz')?.textContent).toContain('Rank 5');
  });

  it('names the guild an in-game rank belongs to, so a multi-guild community is never guessing', async () => {
    const { host } = await createWith(() => of(page([entry()])));

    expect(host.querySelector('.rank-blizz')?.getAttribute('title')).toBe(
      'In-game rank in Emberfall',
    );
  });

  it('shows a dash for a character in no guild this community follows', async () => {
    const { host } = await createWith(() =>
      of(page([entry({ blizzardRank: null, blizzardRankName: null, guildName: null })])),
    );

    expect(host.querySelector('.rank-blizz')?.textContent?.trim()).toBe('—');
  });

  it('sets the class colour once on the row, so nothing maps a class to a hex in TypeScript', async () => {
    const { host } = await createWith(() => of(page([entry()])));

    const row = host.querySelector('tbody tr') as HTMLElement;
    expect(row.style.getPropertyValue('--class-color')).toBe('#C69B6D');
  });

  it('indents an alt under its main', async () => {
    const { host } = await createWith(() =>
      of(
        page([
          entry(),
          entry({ id: 'entry-2', characterName: 'Thornbite', mainRosterEntryId: 'entry-1' }),
        ]),
      ),
    );

    const rows = host.querySelectorAll('tbody tr');
    expect(rows[0].classList.contains('alt-row')).toBe(false);
    expect(rows[1].classList.contains('alt-row')).toBe(true);
  });

  it('shows a shape-matched skeleton while loading', async () => {
    const { host } = await createWith(() => new Observable());

    expect(host.querySelectorAll('scribe-skeleton').length).toBeGreaterThan(0);
  });

  it('shows stale data with its age rather than hiding it — the degraded state', async () => {
    const elevenDaysAgo = new Date(Date.now() - 11 * 24 * 60 * 60 * 1000).toISOString();
    const { host } = await createWith(() => of(page([entry({ lastSyncedAt: elevenDaysAgo })])));

    // The row is still there, with a word saying how old it is. Never a blank error over data we hold.
    expect(host.querySelector('.char-name')?.textContent).toContain('Thornwake');
    expect(host.textContent).toContain('Stale 11d');
  });

  it('renders the empty state with the next action rather than "no results"', async () => {
    const { host } = await createWith(() => of(page([])));

    expect(host.textContent).toContain('Nobody is on this roster yet');
  });

  it('offers a retry when the request genuinely failed', async () => {
    const { host } = await createWith(() => throwError(() => new Error('offline')));

    expect(host.textContent).toContain("Couldn't load this roster");
    expect(host.textContent).toContain('Try again');
  });

  it('hides the officer controls from a member', async () => {
    // Cosmetics, not enforcement — every write behind these has a TenantOfficer policy. Hiding them
    // is about not offering what would be refused.
    const { host } = await createWith(() => of(page([entry()])), 'Member');

    expect(host.querySelector('.rank-picker')).toBeNull();
    expect(host.querySelector('.note-cell')).toBeNull();
  });

  it('lets an officer assign a rank', async () => {
    const { host, fixture, roster } = await createWith(
      () => of(page([entry({ rankId: null, rankName: null, rankColour: null })])),
      'Officer',
    );

    const picker = host.querySelector('.rank-picker') as HTMLSelectElement;
    expect(picker).not.toBeNull();

    picker.value = RAIDER.id;
    picker.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(roster.setRank).toHaveBeenCalledWith('emberfall', 'entry-1', RAIDER.id);
    expect(host.querySelector('.rank')?.textContent).toContain('Raider');
  });

  it('clears a rank when an officer picks "No rank"', async () => {
    const { host, fixture, roster } = await createWith(() => of(page([entry()])), 'Officer');

    const picker = host.querySelector('.rank-picker') as HTMLSelectElement;
    picker.value = '';
    picker.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(roster.setRank).toHaveBeenCalledWith('emberfall', 'entry-1', null);
  });

  it('sends a blank note as null so "cleared" and "never set" stay one state', async () => {
    const { fixture, roster } = await createWith(
      () => of(page([entry({ officerNote: 'Keeps missing raids' })])),
      'Officer',
    );

    fixture.componentInstance.saveNote(entry({ officerNote: 'Keeps missing raids' }), '   ');

    expect(roster.setOfficerNote).toHaveBeenCalledWith('emberfall', 'entry-1', null);
  });

  it('keeps the roster on screen when a WRITE fails', async () => {
    // The characteristic mistake in this app, arriving via a mutation instead of a read: a failed
    // rank assignment must not replace a roster we successfully loaded with a full-page error. A 403
    // here is a real case, not a hypothetical — an officer demoted while this page was open still
    // has the control rendered.
    const { host, fixture, roster } = await createWith(() => of(page([entry()])), 'Officer');
    roster.setRank.mockReturnValue(throwError(() => new Error('forbidden')));

    const picker = host.querySelector('.rank-picker') as HTMLSelectElement;
    picker.value = '';
    picker.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    // The rows are still there, and the failure is reported beside them.
    expect(host.querySelector('tbody tr')).not.toBeNull();
    expect(host.querySelector('.char-name')?.textContent).toContain('Thornwake');
    expect(host.querySelector('.write-error')?.textContent).toContain("Couldn't change");
    expect(host.textContent).not.toContain("Couldn't load this roster");
  });

  it('clears the write error once a later write succeeds', async () => {
    const { host, fixture, roster } = await createWith(() => of(page([entry()])), 'Officer');
    roster.setRank.mockReturnValueOnce(throwError(() => new Error('offline')));

    const picker = host.querySelector('.rank-picker') as HTMLSelectElement;
    picker.value = '';
    picker.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(host.querySelector('.write-error')).not.toBeNull();

    picker.value = RAIDER.id;
    picker.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(host.querySelector('.write-error')).toBeNull();
  });

  it('drops the cursor when the sort changes, so a stale position is never sent', async () => {
    const { fixture, roster } = await createWith(() => of(page([entry()])));

    fixture.componentInstance.selectSort('ItemLevel');
    fixture.detectChanges();

    // A position from one ordering means nothing in another. The server refuses a mismatched cursor
    // anyway, but sending one would be asking for a page we already know is wrong.
    expect(roster.listRoster).toHaveBeenCalledWith('emberfall', { sort: 'ItemLevel' });
  });
});

// 7.5b. Four claim states on a row, and the one that matters most is the third: who holds a claim is
// shown to every member, but the ACTION on somebody else's claim is officers-only.
describe('RosterTable claim state', () => {
  function claimHarness(
    row: RosterEntryServiceModel,
    role: 'Member' | 'Officer' = 'Member',
    claimOverrides: Partial<Record<'claim' | 'release' | 'clearHolder', ReturnType<typeof vi.fn>>> = {},
  ) {
    const roster = {
      listRoster: vi.fn(() => of({ items: [row], nextCursor: null, hasMore: false })),
      listRanks: vi.fn(() => of([RAIDER])),
      setRank: vi.fn(() => of(void 0)),
      setOfficerNote: vi.fn(() => of(void 0)),
    };
    const claims = {
      getClaim: vi.fn(() => of({ characterId: row.characterId, claimedByUserId: null, claimedByDisplayName: null, claimedAt: null })),
      claim: claimOverrides.claim ?? vi.fn(() => of({ characterId: row.characterId, claimedByUserId: 'u1', claimedByDisplayName: 'Me', claimedAt: new Date().toISOString() })),
      release: claimOverrides.release ?? vi.fn(() => of(void 0)),
      clearHolder: claimOverrides.clearHolder ?? vi.fn(() => of(void 0)),
    };
    const currentUser = {
      user: () => ({
        id: 'u1',
        memberships: [
          { tenantId: 't', tenantSlug: 'emberfall', tenantName: 'Emberfall', role, joinedAt: '' },
        ],
      }),
    };

    TestBed.configureTestingModule({
      imports: [RosterTable],
      providers: [
        provideRouter([]),
        { provide: RosterService, useValue: roster },
        { provide: ClaimService, useValue: claims },
        { provide: CurrentUserService, useValue: currentUser },
      ],
    });

    const fixture = TestBed.createComponent(RosterTable);
    fixture.componentRef.setInput('tenantSlug', 'emberfall');
    fixture.detectChanges();

    return { fixture, claims, host: fixture.nativeElement as HTMLElement };
  }

  function claimActions(host: HTMLElement): string[] {
    return [...host.querySelectorAll('.claim-cell button')].map((b) => b.textContent?.trim() ?? '');
  }

  it('offers Claim on an unclaimed row to any member', () => {
    const { host } = claimHarness(entry({ claimedByUserId: null, claimedByDisplayName: null }));

    expect(claimActions(host)).toContain('Claim');
  });

  it('shows "You" and offers Unclaim on my own claim', () => {
    const { host } = claimHarness(entry({ claimedByUserId: 'u1', claimedByDisplayName: 'Me' }));

    expect(host.querySelector('.claimed-by')?.textContent).toContain('You');
    expect(claimActions(host)).toContain('Unclaim');
  });

  it("names the holder but gives a MEMBER no action on somebody else's claim", () => {
    const { host } = claimHarness(
      entry({ claimedByUserId: 'u2', claimedByDisplayName: 'Thornbite' }),
      'Member',
    );

    // Who holds it is not privileged information inside the community; acting on it is.
    expect(host.querySelector('.claimed-by')?.textContent).toContain('Thornbite');
    expect(claimActions(host)).toEqual([]);
  });

  it("gives an OFFICER a Clear action on somebody else's claim", () => {
    const { host } = claimHarness(
      entry({ claimedByUserId: 'u2', claimedByDisplayName: 'Thornbite' }),
      'Officer',
    );

    expect(claimActions(host)).toContain('Clear');
  });

  it('keeps the roster on screen when a claim fails', () => {
    // A 409 means somebody claimed it first — a normal race, not a reason to lose the page.
    const { host, fixture } = claimHarness(
      entry({ claimedByUserId: null, claimedByDisplayName: null }),
      'Member',
      { claim: vi.fn(() => throwError(() => new Error('conflict'))) },
    );

    (host.querySelector('.claim-cell button') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(host.querySelector('tbody tr')).not.toBeNull();
    expect(host.querySelector('.write-error')?.textContent).toContain('claimed it first');
    expect(host.textContent).not.toContain("Couldn't load this roster");
  });

  it('updates the row in place after a successful claim', () => {
    const { host, fixture } = claimHarness(entry({ claimedByUserId: null, claimedByDisplayName: null }));

    (host.querySelector('.claim-cell button') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(host.querySelector('.claimed-by')?.textContent).toContain('You');
    expect(claimActions(host)).toContain('Unclaim');
  });
});
