import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';
import { RosterService } from '../../../core/roster.service';
import { GuildRankNameServiceModel, TenantRankServiceModel } from '../../../models/roster.models';
import { RankManager } from './rank-manager';

const RAIDER: TenantRankServiceModel = {
  id: 'r1',
  name: 'Raider',
  sortOrder: 10,
  colour: '#cba76a',
};
const TRIAL: TenantRankServiceModel = { id: 'r2', name: 'Trial', sortOrder: 20, colour: '#616d7e' };

function guildRanks(names: Partial<Record<number, string>> = {}): GuildRankNameServiceModel[] {
  return Array.from({ length: 10 }, (_, rank) => ({
    guildId: 'g1',
    guildName: 'Emberfall',
    rank,
    name: names[rank] ?? null,
  }));
}

interface Harness {
  fixture: ComponentFixture<RankManager>;
  roster: {
    listRanks: ReturnType<typeof vi.fn>;
    createRank: ReturnType<typeof vi.fn>;
    updateRank: ReturnType<typeof vi.fn>;
    deleteRank: ReturnType<typeof vi.fn>;
    listGuildRankNames: ReturnType<typeof vi.fn>;
    setGuildRankName: ReturnType<typeof vi.fn>;
  };
  host: HTMLElement;
}

async function createWith(
  options: {
    ranks?: () => Observable<TenantRankServiceModel[]>;
    guildRankNames?: () => Observable<GuildRankNameServiceModel[]>;
  } = {},
): Promise<Harness> {
  const roster = {
    listRanks: vi.fn(options.ranks ?? (() => of([RAIDER, TRIAL]))),
    createRank: vi.fn(() => of({ id: 'r3', name: 'Social', sortOrder: 30, colour: '#3e9c77' })),
    updateRank: vi.fn(() => of(RAIDER)),
    deleteRank: vi.fn(() => of(void 0)),
    listGuildRankNames: vi.fn(options.guildRankNames ?? (() => of(guildRanks()))),
    setGuildRankName: vi.fn(() => of(void 0)),
  };

  await TestBed.configureTestingModule({
    imports: [RankManager],
    providers: [{ provide: RosterService, useValue: roster }],
  }).compileComponents();

  const fixture = TestBed.createComponent(RankManager);
  fixture.componentRef.setInput('tenantSlug', 'emberfall');
  fixture.detectChanges();

  return { fixture, roster, host: fixture.nativeElement as HTMLElement };
}

describe('RankManager', () => {
  it('keeps the two ladders visibly separate and says which is which', async () => {
    // The screen's whole reason for existing: the community's ladder and the game's rank names are
    // the two things called "rank" here, and nothing may suggest one derives from the other.
    const { host } = await createWith();

    expect(host.textContent).toContain('Your ladder');
    expect(host.textContent).toContain('In-game rank names');
    expect(host.textContent).toContain('Blizzard reports a guild');
  });

  it('lists the community ladder in sort order', async () => {
    const { host } = await createWith();

    const pills = [...host.querySelectorAll('.rank')].map((pill) => pill.textContent?.trim());
    expect(pills[0]).toContain('Raider');
    expect(pills[1]).toContain('Trial');
  });

  it('renders all ten in-game ranks, named or not', async () => {
    // The editor is a form over a fixed range, so an unnamed rank is a blank to fill in rather than a
    // row that is missing.
    const { host } = await createWith({ guildRankNames: () => of(guildRanks({ 3: 'Veteran' })) });

    expect(host.querySelectorAll('.rm-rank-row').length).toBe(10);
    const inputs = [...host.querySelectorAll<HTMLInputElement>('.rm-rank-row input')];
    expect(inputs[3].value).toBe('Veteran');
    expect(inputs[4].value).toBe('');
    // The number stays visible beside the name, so nobody mistakes our label for Blizzard's.
    expect(host.querySelector('.rm-rank-number')?.textContent?.trim()).toBe('0');
  });

  it('refuses a duplicate rank name before spending a request on it', async () => {
    const { fixture, host, roster } = await createWith();

    fixture.componentInstance.newRankName.set('raider');
    fixture.detectChanges();

    expect(fixture.componentInstance.canCreate()).toBe(false);
    expect(host.textContent).toContain('already has a rank with that name');

    fixture.componentInstance.createRank();
    expect(roster.createRank).not.toHaveBeenCalled();
  });

  it('creates a rank at the end of the ladder', async () => {
    const { fixture, roster } = await createWith();

    fixture.componentInstance.newRankName.set('Social');
    fixture.componentInstance.createRank();

    expect(roster.createRank).toHaveBeenCalledWith('emberfall', {
      name: 'Social',
      sortOrder: 30,
      colour: '#CBA76A',
    });
  });

  it('reorders by swapping positions with the neighbour, touching nothing else', async () => {
    const { fixture, roster } = await createWith();

    fixture.componentInstance.move(TRIAL, -1);

    // Trial takes Raider's position and Raider takes Trial's. A renumber-everything approach would
    // rewrite rows nobody asked to change.
    expect(roster.updateRank).toHaveBeenCalledWith('emberfall', TRIAL.id, {
      name: 'Trial',
      sortOrder: 10,
      colour: '#616d7e',
    });
    expect(roster.updateRank).toHaveBeenCalledWith('emberfall', RAIDER.id, {
      name: 'Raider',
      sortOrder: 20,
      colour: '#cba76a',
    });
  });

  it('clears an in-game rank name by sending null rather than an empty string', async () => {
    const { fixture, roster } = await createWith({
      guildRankNames: () => of(guildRanks({ 3: 'Veteran' })),
    });

    const row = guildRanks({ 3: 'Veteran' })[3];
    fixture.componentInstance.setGuildRankName(row, '   ');

    // "Never named" and "named then cleared" stay one state, so the UI has one blank to render.
    expect(roster.setGuildRankName).toHaveBeenCalledWith('emberfall', 'g1', 3, null);
  });

  it('says what to do when the community follows no guilds', async () => {
    const { host } = await createWith({ guildRankNames: () => of([]) });

    expect(host.textContent).toContain('No guilds linked');
  });

  it('keeps the ladder on screen when a WRITE fails, and names the likely reason', async () => {
    // A 409 on delete means roster entries still hold the rank. Wiping the editor an officer is
    // mid-edit in — because one save failed — is the "blank error over data we still hold" mistake.
    const { host, fixture, roster } = await createWith();
    roster.deleteRank.mockReturnValue(throwError(() => new Error('conflict')));

    fixture.componentInstance.deleteRank(RAIDER);
    fixture.detectChanges();

    expect(host.querySelectorAll('.rank').length).toBeGreaterThan(0);
    expect(host.querySelector('.rm-write-error')?.textContent).toContain('may still hold it');
    expect(host.textContent).not.toContain("Couldn't load rank settings");
  });

  it('offers a retry when the ladder fails to load', async () => {
    const { host } = await createWith({ ranks: () => throwError(() => new Error('offline')) });

    expect(host.textContent).toContain("Couldn't load rank settings");
    expect(host.textContent).toContain('Try again');
  });
});
