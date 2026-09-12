import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { CharacterDetailServiceModel } from '../models/character.models';
import { RuntimeConfigService } from './runtime-config.service';

// Character is global reference data (tenancy.md) -- tenant-less, no /t/{tenantSlug} segment.
@Injectable({ providedIn: 'root' })
export class CharacterService {
  private readonly http = inject(HttpClient);
  private readonly runtimeConfig = inject(RuntimeConfigService);

  getCharacter(region: string, realmSlug: string, name: string): Observable<CharacterDetailServiceModel> {
    const url = `${this.runtimeConfig.gatewayUrl()}/api/v1/characters/${realmSlug}/${name}`;
    return this.http.get<CharacterDetailServiceModel>(url, { params: { region } });
  }
}
