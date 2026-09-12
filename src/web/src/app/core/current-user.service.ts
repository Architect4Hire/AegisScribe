import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { UserServiceModel } from '../models/auth.models';
import { RuntimeConfigService } from './runtime-config.service';

// The one place the signed-in user and their tenant memberships are cached, so the auth guard and
// the tenant guard both read from a single GET /api/v1/me rather than each fetching it themselves.
// See .claude/rules/auth.md -> "Angular side".
@Injectable({ providedIn: 'root' })
export class CurrentUserService {
  private readonly http = inject(HttpClient);
  private readonly runtimeConfig = inject(RuntimeConfigService);

  private readonly _user = signal<UserServiceModel | null>(null);
  readonly user = this._user.asReadonly();

  private pending: Promise<UserServiceModel> | null = null;

  // Returns the cached user if one is loaded; otherwise fetches once. Concurrent callers (the two
  // guards typically run back-to-back on the same navigation) share the one in-flight request
  // rather than firing a second GET /me.
  async ensureLoaded(): Promise<UserServiceModel> {
    const cached = this._user();
    if (cached) {
      return cached;
    }

    this.pending ??= this.load();
    try {
      return await this.pending;
    } finally {
      this.pending = null;
    }
  }

  // Called by the interceptor on a 401 — the session ended, so the cached identity is stale.
  clear(): void {
    this._user.set(null);
    this.pending = null;
  }

  hasMembership(tenantSlug: string): boolean {
    return this._user()?.memberships.some((membership) => membership.tenantSlug === tenantSlug) ?? false;
  }

  private async load(): Promise<UserServiceModel> {
    const user = await firstValueFrom(
      this.http.get<UserServiceModel>(`${this.runtimeConfig.gatewayUrl()}/api/v1/me`),
    );
    this._user.set(user);
    return user;
  }
}
