import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  CreateTenantViewModel,
  SlugCheckServiceModel,
  TenantServiceModel,
} from '../models/auth.models';
import { RuntimeConfigService } from './runtime-config.service';

// Creating a community is tenant-less — there is no /t/{slug} to resolve into until it exists
// (TenantsController). Everything here goes through the gateway like every other call; the SPA never
// addresses api.* (.claude/rules/frontend.md).
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

  // Exactly one of the two, per SlugCheckViewModelValidator: `name` asks what slug a community would
  // get and whether it's free, `slug` asks about one the user has edited. Sending both is a 400.
  checkSlug(query: { name: string } | { slug: string }): Observable<SlugCheckServiceModel> {
    return this.http.get<SlugCheckServiceModel>(
      `${this.runtimeConfig.gatewayUrl()}/api/v1/tenants/slug-check`,
      { params: { ...query } },
    );
  }
}
