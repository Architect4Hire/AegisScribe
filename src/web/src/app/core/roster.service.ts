import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ImportGuildRosterViewModel, RosterImportServiceModel } from '../models/guild.models';
import {
  AddRosterEntryViewModel,
  CursorPageServiceModel,
  GuildRankNameServiceModel,
  RankViewModel,
  RosterEntryServiceModel,
  RosterSort,
  TenantRankServiceModel,
} from '../models/roster.models';
import { skipTenantMissRedirect } from './auth-redirect.context';
import { RuntimeConfigService } from './runtime-config.service';

// Everything a community owns lives under /t/{tenantSlug}, and the slug comes from the active route
// rather than a stored variable, so a switched community can never leave a request pointing at the
// previous one (frontend.md).
//
// Every call goes through the gateway. The SPA never addresses api.* — that hostname is for the mobile
// app and for ops, and a browser request to it would be a bug.
@Injectable({ providedIn: 'root' })
export class RosterService {
  private readonly http = inject(HttpClient);
  private readonly runtimeConfig = inject(RuntimeConfigService);

  listRoster(
    tenantSlug: string,
    options: { sort?: RosterSort; cursor?: string | null; limit?: number } = {},
  ): Observable<CursorPageServiceModel<RosterEntryServiceModel>> {
    let params: Record<string, string> = {};

    if (options.sort) {
      params = { ...params, sort: options.sort };
    }

    // Passed back exactly as received. The cursor is opaque and carries its own sort, so a stale one
    // from a different ordering restarts the page server-side rather than seeking somewhere wrong.
    if (options.cursor) {
      params = { ...params, cursor: options.cursor };
    }

    if (options.limit !== undefined) {
      params = { ...params, limit: String(options.limit) };
    }

    return this.http.get<CursorPageServiceModel<RosterEntryServiceModel>>(
      `${this.base(tenantSlug)}/roster`,
      { params },
    );
  }

  // Officer-only server-side. The UI hides these behind a role check, but that is cosmetics — every
  // one of them has a TenantOfficer policy behind it (.claude/rules/auth.md).
  //
  // A 404 means the character id is unknown and a 409 that it is already on the roster — both answers
  // about the character, so neither may bounce the officer to the tenant picker.
  addEntry(tenantSlug: string, viewModel: AddRosterEntryViewModel): Observable<void> {
    return this.http.post<void>(`${this.base(tenantSlug)}/roster`, viewModel, {
      context: skipTenantMissRedirect(),
    });
  }

  // Reaches Blizzard for nothing and costs no budget: the members were persisted by the guild sync the
  // link already paid for. A 404 means this community does not follow that guild (any more).
  importFromGuild(
    tenantSlug: string,
    viewModel: ImportGuildRosterViewModel,
  ): Observable<RosterImportServiceModel> {
    return this.http.post<RosterImportServiceModel>(
      `${this.base(tenantSlug)}/roster/import`,
      viewModel,
      { context: skipTenantMissRedirect() },
    );
  }

  setRank(
    tenantSlug: string,
    rosterEntryId: string,
    tenantRankId: string | null,
  ): Observable<void> {
    return this.http.put<void>(`${this.base(tenantSlug)}/roster/${rosterEntryId}/rank`, {
      tenantRankId,
    });
  }

  // Its own call rather than half of a combined update, because a request that carried both fields
  // could not distinguish "leave the note alone" from "clear it".
  setOfficerNote(
    tenantSlug: string,
    rosterEntryId: string,
    officerNote: string | null,
  ): Observable<void> {
    return this.http.put<void>(`${this.base(tenantSlug)}/roster/${rosterEntryId}/note`, {
      officerNote,
    });
  }

  removeEntry(tenantSlug: string, rosterEntryId: string): Observable<void> {
    return this.http.delete<void>(`${this.base(tenantSlug)}/roster/${rosterEntryId}`);
  }

  listRanks(tenantSlug: string): Observable<TenantRankServiceModel[]> {
    return this.http.get<TenantRankServiceModel[]>(`${this.base(tenantSlug)}/ranks`);
  }

  createRank(tenantSlug: string, viewModel: RankViewModel): Observable<TenantRankServiceModel> {
    return this.http.post<TenantRankServiceModel>(`${this.base(tenantSlug)}/ranks`, viewModel);
  }

  updateRank(
    tenantSlug: string,
    rankId: string,
    viewModel: RankViewModel,
  ): Observable<TenantRankServiceModel> {
    return this.http.put<TenantRankServiceModel>(
      `${this.base(tenantSlug)}/ranks/${rankId}`,
      viewModel,
    );
  }

  deleteRank(tenantSlug: string, rankId: string): Observable<void> {
    return this.http.delete<void>(`${this.base(tenantSlug)}/ranks/${rankId}`);
  }

  // What this community calls the in-game ranks of the guilds it follows. A separate ladder from the
  // one above and never derived from it.
  listGuildRankNames(tenantSlug: string): Observable<GuildRankNameServiceModel[]> {
    return this.http.get<GuildRankNameServiceModel[]>(`${this.base(tenantSlug)}/guild-rank-names`);
  }

  setGuildRankName(
    tenantSlug: string,
    guildId: string,
    rank: number,
    name: string | null,
  ): Observable<void> {
    return this.http.put<void>(`${this.base(tenantSlug)}/guild-rank-names/${guildId}/${rank}`, {
      name,
    });
  }

  private base(tenantSlug: string): string {
    return `${this.runtimeConfig.gatewayUrl()}/api/v1/t/${tenantSlug}`;
  }
}
