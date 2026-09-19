import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';
import { CurrentUserService } from '../../../core/current-user.service';
import { GuildService } from '../../../core/guild.service';
import { RosterService } from '../../../core/roster.service';
import { TenantService } from '../../../core/tenant.service';
import { TenantRole, UserServiceModel } from '../../../models/auth.models';
import { GuildServiceModel, RosterImportServiceModel } from '../../../models/guild.models';
import { GuildOverview } from './guild-overview';

const DAY_MS = 24 * 60 * 60 * 1000;

function guild(over: Partial<GuildServiceModel> = {}): GuildServiceModel {
  return {
    id: 'g1',
    name: 'Ashes of Dawn',
    realmSlug: 'argent-dawn',
    faction: 'Alliance',
    memberCount: 42,
    lastSyncedAt: new Date().toISOString(),
    ...over,
  };
}

const IMPORTED: RosterImportServiceModel = {
  imported: 12,
  alreadyOnRoster: 30,
  notInAnyLinkedGuild: [{ rosterEntryId: 'e9', characterName: 'Wanderer', realmSlug: 'argent-dawn' }],
  notInAnyLinkedGuildCount: 1,
};

function signedInAs(role: TenantRole, slug = 'emberfall'): UserServiceModel {
  return {
    id: 'u-me',
    email: 'me@example.test',
    displayName: 'Me',
    createdAt: '2026-01-01T00:00:00Z',
    memberships: [
      { tenantId: 't-1', tenantSlug: slug, tenantName: slug, role, joinedAt: '2026-01-01T00:00:00Z' },
    ],
  };
}

function httpError(status: number, body: unknown = null, headers?: Record<string, string>) {
  return new HttpErrorResponse({ status, error: body, headers: new HttpHeaders(headers) });
}

interface Harness {
  fixture: ComponentFixture<GuildOverview>;
  component: GuildOverview;
  guilds: {
    list: ReturnType<typeof vi.fn>;
    link: ReturnType<typeof vi.fn>;
    resync: ReturnType<typeof vi.fn>;
    unlink: ReturnType<typeof vi.fn>;
  };
  roster: { importFromGuild: ReturnType<typeof vi.fn> };
  tenant: { getOverview: ReturnType<typeof vi.fn> };
  host: HTMLElement;
}

async function createWith(
  options: {
    role?: TenantRole;
    list?: () => Observable<GuildServiceModel[]>;
    link?: () => Observable<GuildServiceModel>;
    blizzardConfigured?: boolean;
  } = {},
): Promise<Harness> {
  const guilds = {
    list: vi.fn(options.list ?? (() => of([guild()]))),
    link: vi.fn(options.link ?? (() => of(guild({ id: 'g2', name: 'Emberfall Vanguard' })))),
    resync: vi.fn(() => of(guild({ memberCount: 43 }))),
    unlink: vi.fn(() => of(void 0)),
  };
  const roster = { importFromGuild: vi.fn(() => of(IMPORTED)) };
  const tenant = {
    getOverview: vi.fn(() =>
      of({
        hasLinkedGuild: true,
        rankCount: 0,
        rosterCount: 0,
        memberCount: 1,
        blizzardConfigured: options.blizzardConfigured ?? true,
      }),
    ),
  };
  const user = signedInAs(options.role ?? 'Owner');

  await TestBed.configureTestingModule({
    imports: [GuildOverview],
    providers: [
      provideRouter([]),
      { provide: GuildService, useValue: guilds },
      { provide: RosterService, useValue: roster },
      { provide: TenantService, useValue: tenant },
      { provide: CurrentUserService, useValue: { user: () => user } },
    ],
  }).compileComponents();

  const fixture = TestBed.createComponent(GuildOverview);
  fixture.componentRef.setInput('tenantSlug', 'emberfall');
  fixture.detectChanges();

  return {
    fixture,
    component: fixture.componentInstance,
    guilds,
    roster,
    tenant,
    host: fixture.nativeElement as HTMLElement,
  };
}

function fillLinkForm(component: GuildOverview, realm: string, name: string): void {
  component.region.set('eu');
  component.realm.set(realm);
  component.guildName.set(name);
}

describe('GuildOverview', () => {
  it("lists the active community's guilds with how fresh each roster is", async () => {
    const { host, guilds } = await createWith();

    // The slug comes from the route input, never from somewhere that could name another community.
    expect(guilds.list).toHaveBeenCalledWith('emberfall');
    expect(host.textContent).toContain('Ashes of Dawn');
    expect(host.textContent).toContain('42 members');
    expect(host.textContent).toContain('Synced today');
  });

  it('shows a stale roster with its age rather than hiding it (the degraded state)', async () => {
    const old = new Date(Date.now() - 12 * DAY_MS).toISOString();
    const { host } = await createWith({ list: () => of([guild({ lastSyncedAt: old })]) });

    expect(host.textContent).toContain('Stale 12d');
    expect(host.textContent).toContain('Ashes of Dawn');
  });

  it('offers a retry when the list cannot be loaded', async () => {
    const { host } = await createWith({ list: () => throwError(() => httpError(500)) });

    expect(host.textContent).toContain("Couldn't load this community's guilds");
    expect(host.textContent).toContain('Try again');
  });

  it('shows a plain member the list but none of the officer controls, and asks nothing officer-only', async () => {
    const { host, tenant } = await createWith({ role: 'Member' });

    expect(host.textContent).toContain('Ashes of Dawn');
    expect(host.querySelector('.go-link-form')).toBeNull();
    expect(host.querySelector('.go-actions')).toBeNull();
    expect(tenant.getOverview).not.toHaveBeenCalled();
  });

  it('links a guild, sending the realm as a slug, and adds it to the list', async () => {
    const { fixture, component, guilds, host } = await createWith();

    fillLinkForm(component, "Mal'Ganis", '  Emberfall Vanguard ');
    component.link();
    fixture.detectChanges();

    expect(guilds.link).toHaveBeenCalledWith('emberfall', {
      region: 'eu',
      realmSlug: 'malganis',
      guildName: 'Emberfall Vanguard',
    });
    expect(host.textContent).toContain('Emberfall Vanguard');
    expect(host.textContent).toContain('Import them to put them on the roster');
    // Region and realm stay for the next one; the guild name clears.
    expect(component.realm()).toBe("Mal'Ganis");
    expect(component.guildName()).toBe('');
  });

  it('names the guild Blizzard could not find, and keeps what was typed', async () => {
    const { fixture, component, host } = await createWith({
      link: () => throwError(() => httpError(404)),
    });

    fillLinkForm(component, 'Argent Dawn', 'Ashes of Dusk');
    component.link();
    fixture.detectChanges();

    expect(host.querySelector('[role="alert"]')?.textContent).toContain(
      'Blizzard has no guild called "Ashes of Dusk" on argent-dawn (EU)',
    );
    expect(component.guildName()).toBe('Ashes of Dusk');
  });

  it('does not blame a typo when the deployment has no Blizzard credentials', async () => {
    const { fixture, component, host } = await createWith({
      blizzardConfigured: false,
      link: () => throwError(() => httpError(404)),
    });

    expect(host.querySelector('.go-caveat')?.textContent).toContain('no Blizzard credentials');

    fillLinkForm(component, 'Argent Dawn', 'Ashes of Dawn');
    component.link();
    fixture.detectChanges();

    expect(host.querySelector('[role="alert"]')?.textContent).toContain('no Blizzard credentials');
    expect(host.querySelector('[role="alert"]')?.textContent).not.toContain('Check the spelling');
  });

  it('says when the sync budget is spent and when to come back', async () => {
    const { fixture, component, host } = await createWith({
      link: () =>
        throwError(() =>
          httpError(
            429,
            { type: 'https://api.aegisscribe.com/problems/tenant-sync-budget-exhausted' },
            { 'Retry-After': '90' },
          ),
        ),
    });

    fillLinkForm(component, 'Argent Dawn', 'Ashes of Dawn');
    component.link();
    fixture.detectChanges();

    expect(host.querySelector('[role="alert"]')?.textContent).toContain(
      'used its Blizzard sync budget for now. Try again in 2 minutes.',
    );
  });

  it('imports members and reports what it did, including what it deliberately left alone', async () => {
    const { fixture, component, roster, host } = await createWith();

    component.importMembers(component.guilds()[0]);
    fixture.detectChanges();

    expect(roster.importFromGuild).toHaveBeenCalledWith('emberfall', { guildId: 'g1' });
    expect(host.textContent).toContain('Imported 12');
    expect(host.textContent).toContain('30 already on the roster');
    expect(host.textContent).toContain('1 roster entry is in none of the guilds');
    expect(host.textContent).toContain('Nothing was removed');
    expect(host.textContent).toContain('Wanderer');
  });

  it('re-syncs a guild in place', async () => {
    const { fixture, component, guilds, host } = await createWith();

    component.resync(component.guilds()[0]);
    fixture.detectChanges();

    expect(guilds.resync).toHaveBeenCalledWith('emberfall', 'g1');
    expect(host.textContent).toContain('43 members');
  });

  it('unlinks a guild and drops it from the list', async () => {
    const { fixture, component, guilds, host } = await createWith();

    component.unlink(component.guilds()[0]);
    fixture.detectChanges();

    expect(guilds.unlink).toHaveBeenCalledWith('emberfall', 'g1');
    expect(host.querySelector('.go-guild')).toBeNull();
    expect(host.textContent).toContain('No guilds linked yet');
  });
});
