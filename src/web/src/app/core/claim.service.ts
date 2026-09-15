import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { CharacterClaimServiceModel } from '../models/roster.models';
import { RuntimeConfigService } from './runtime-config.service';

// "That character is mine", inside one community.
//
// Its own service rather than more surface on RosterService: claims are a separate vertical on the
// server, and two unrelated screens consume them. The tenant slug comes from the caller's active route
// rather than a stored variable (frontend.md).
@Injectable({ providedIn: 'root' })
export class ClaimService {
  private readonly http = inject(HttpClient);
  private readonly runtimeConfig = inject(RuntimeConfigService);

  // Always 200: an unclaimed character is a real answer with null fields, and a 404 would leave the
  // caller unable to tell "nobody has claimed this" from "no such character".
  getClaim(tenantSlug: string, characterId: string): Observable<CharacterClaimServiceModel> {
    return this.http.get<CharacterClaimServiceModel>(`${this.base(tenantSlug)}/${characterId}`);
  }

  // Claims for the SIGNED-IN user. There is no field naming a user and there will not be one — the
  // server takes the claimant from the token, and a claim is the proof of ownership behind a global
  // erasure request.
  claim(tenantSlug: string, characterId: string): Observable<CharacterClaimServiceModel> {
    return this.http.post<CharacterClaimServiceModel>(this.base(tenantSlug), { characterId });
  }

  // Releases the caller's OWN claim. Releasing someone else's is a 403 from the server.
  release(tenantSlug: string, characterId: string): Observable<void> {
    return this.http.delete<void>(`${this.base(tenantSlug)}/${characterId}`);
  }

  // An officer frees whoever holds it, and the act is audited. It FREES the claim rather than handing
  // it to anyone, which the API enforces regardless of what the UI offers.
  clearHolder(tenantSlug: string, characterId: string): Observable<void> {
    return this.http.delete<void>(`${this.base(tenantSlug)}/${characterId}/holder`);
  }

  private base(tenantSlug: string): string {
    return `${this.runtimeConfig.gatewayUrl()}/api/v1/t/${tenantSlug}/claims`;
  }
}
