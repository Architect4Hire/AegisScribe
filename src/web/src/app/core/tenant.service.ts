import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  CreateTenantViewModel,
  SlugCheckServiceModel,
  TenantOverviewServiceModel,
  TenantServiceModel,
} from '../models/auth.models';
import { RuntimeConfigService } from './runtime-config.service';

// The community itself: creating one, and asking what an existing one has.
//
// The first two calls are tenant-LESS on purpose — there is no /t/{slug} to resolve into until the
// community exists (TenantsController). `getOverview` is the exception and takes the slug from the
// caller's active route, never from a stored variable, so a switched community cannot leave a request
// pointing at the previous one (frontend.md).
//
// Everything here goes through the gateway like every other call; the SPA never addresses api.*.
@Injectable({ providedIn: 'root' })
export class TenantService {
  private readonly http = inject(HttpClient);
  private readonly runtimeConfig = inject(RuntimeConfigService);

  create(viewModel: CreateTenantViewModel): Observable<TenantServiceModel> {
    return this.http.post<TenantServiceModel>(
      `${this.runtimeConfig.gatewayUrl()}/api/v1/tenants`,
      viewModel,
    );
  }

  // What this community HAS — the one read behind the first-run checklist, composed server-side so
  // the first screen a new community renders costs one round trip rather than five.
  //
  // TenantOfficer server-side: a plain member gets a 403 here and sees the ordinary per-screen empty
  // states instead of somebody else's setup list. The caller is expected not to ask at all in that
  // case — hiding the panel is about not making a request that will be refused, not about concealing
  // anything.
  getOverview(tenantSlug: string): Observable<TenantOverviewServiceModel> {
    return this.http.get<TenantOverviewServiceModel>(
      `${this.runtimeConfig.gatewayUrl()}/api/v1/t/${tenantSlug}/overview`,
    );
  }

  // Exactly one of the two, per SlugCheckViewModelValidator: `name` asks what slug a community would
  // get and whether it's free, `slug` asks about one the user has edited. Sending both is a 400.
  checkSlug(query: { name: string } | { slug: string }): Observable<SlugCheckServiceModel> {
    return this.http.get<SlugCheckServiceModel>(
      `${this.runtimeConfig.gatewayUrl()}/api/v1/tenants/slug-check`,
      { params: { ...query } },
    );
  }
}
