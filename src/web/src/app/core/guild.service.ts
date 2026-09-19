import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { GuildServiceModel, LinkGuildViewModel } from '../models/guild.models';
import { skipTenantMissRedirect } from './auth-redirect.context';
import { RuntimeConfigService } from './runtime-config.service';

// The guilds a community follows. Everything here lives under /t/{slug} and takes the slug from the
// caller's active route, never a stored variable (frontend.md).
//
// The three writes opt out of the interceptor's tenant-miss redirect, because their 404 is an answer
// about the GUILD: Blizzard knows no guild by that name, or this community no longer follows it. The
// list keeps the backstop — a 404 there really does mean "not your community".
//
// Every call goes through the gateway. The SPA never addresses api.*, and never Blizzard: linking and
// re-syncing ask the API to make the Blizzard call, under the community's sync budget.
@Injectable({ providedIn: 'root' })
export class GuildService {
  private readonly http = inject(HttpClient);
  private readonly runtimeConfig = inject(RuntimeConfigService);

  // TenantMember server-side.
  list(tenantSlug: string): Observable<GuildServiceModel[]> {
    return this.http.get<GuildServiceModel[]>(this.base(tenantSlug));
  }

  // TenantOfficer. Linking IS the first sync — one Blizzard call fetches the guild and its roster — so
  // it draws on the community's budget and can come back 429 with a Retry-After.
  link(tenantSlug: string, viewModel: LinkGuildViewModel): Observable<GuildServiceModel> {
    return this.http.post<GuildServiceModel>(this.base(tenantSlug), viewModel, {
      context: skipTenantMissRedirect(),
    });
  }

  // TenantOfficer, and budgeted like link.
  resync(tenantSlug: string, guildId: string): Observable<GuildServiceModel> {
    return this.http.post<GuildServiceModel>(`${this.base(tenantSlug)}/${guildId}/sync`, null, {
      context: skipTenantMissRedirect(),
    });
  }

  // TenantOfficer. Removes this community's link only; the global guild stays for anyone else following
  // it. A 404 means it was already unlinked.
  unlink(tenantSlug: string, guildId: string): Observable<void> {
    return this.http.delete<void>(`${this.base(tenantSlug)}/${guildId}`, {
      context: skipTenantMissRedirect(),
    });
  }

  private base(tenantSlug: string): string {
    return `${this.runtimeConfig.gatewayUrl()}/api/v1/t/${tenantSlug}/guilds`;
  }
}
