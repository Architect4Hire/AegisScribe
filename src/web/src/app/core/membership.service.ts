import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  CreateInvitationViewModel,
  CreatedInvitationServiceModel,
  InvitationServiceModel,
  JoinRequestServiceModel,
  JoinRequestStatus,
  TenantMemberServiceModel,
  TenantRole,
} from '../models/auth.models';
import { CursorPageServiceModel } from '../models/roster.models';
import { RuntimeConfigService } from './runtime-config.service';

// Who is in a community, and the acts that change that: invite, review, promote, remove.
//
// Its own service rather than more surface on TenantService: that one is tenant-less on purpose — a
// community does not exist yet when you create it — whereas everything here lives under /t/{slug} and
// takes the slug from the caller's active route rather than a stored variable, so a switched community
// can never leave a request pointing at the previous one (frontend.md).
//
// Every call goes through the gateway. The SPA never addresses api.*.
//
// InvitationService is the OTHER half of the same vertical and stays separate: accepting is tenant-less
// and anonymous, because the person accepting is not a member of anything yet.
@Injectable({ providedIn: 'root' })
export class MembershipService {
  private readonly http = inject(HttpClient);
  private readonly runtimeConfig = inject(RuntimeConfigService);

  // TenantMember server-side: who is in your community is not privileged information inside it.
  listMembers(
    tenantSlug: string,
    options: { cursor?: string | null; limit?: number } = {},
  ): Observable<CursorPageServiceModel<TenantMemberServiceModel>> {
    let params: Record<string, string> = {};

    // Passed back exactly as received. The cursor is opaque — nothing here decodes or constructs one.
    if (options.cursor) {
      params = { ...params, cursor: options.cursor };
    }

    if (options.limit !== undefined) {
      params = { ...params, limit: String(options.limit) };
    }

    return this.http.get<CursorPageServiceModel<TenantMemberServiceModel>>(
      `${this.base(tenantSlug)}/members`,
      { params },
    );
  }

  // Everything below is TenantOfficer server-side, and WHICH member an officer may act on is narrower
  // still — a rule in Business, because answering it means reading both people's standing. The UI hides
  // what will be refused; it does not decide it.
  setMemberRole(tenantSlug: string, userId: string, role: TenantRole): Observable<void> {
    return this.http.put<void>(`${this.base(tenantSlug)}/members/${userId}/role`, { role });
  }

  // Takes the member's claims and roster entries in THIS community with them; their global character
  // data is untouched. 204 whether or not they were there — DELETE is idempotent.
  removeMember(tenantSlug: string, userId: string): Observable<void> {
    return this.http.delete<void>(`${this.base(tenantSlug)}/members/${userId}`);
  }

  listInvitations(tenantSlug: string): Observable<InvitationServiceModel[]> {
    return this.http.get<InvitationServiceModel[]>(`${this.base(tenantSlug)}/invitations`);
  }

  // The response carries the plaintext token, and it is the only time it is ever on the wire outbound.
  // The server keeps a hash, so a caller that loses it cannot ask again.
  //
  // No Idempotency-Key. The endpoint accepts one and the mobile client will send it, but this is a
  // browser on a desktop network with a button that disables itself, and ClaimService already posts to
  // an [Idempotent] endpoint without one — inventing a key scheme in a single service would be the
  // inconsistency, not the omission.
  createInvitation(
    tenantSlug: string,
    viewModel: CreateInvitationViewModel,
  ): Observable<CreatedInvitationServiceModel> {
    return this.http.post<CreatedInvitationServiceModel>(
      `${this.base(tenantSlug)}/invitations`,
      viewModel,
    );
  }

  // 204 whether or not there was anything live to revoke: already accepted, already revoked and never
  // existed are all the same answer to "make sure this link is dead".
  revokeInvitation(tenantSlug: string, invitationId: string): Observable<void> {
    return this.http.delete<void>(`${this.base(tenantSlug)}/invitations/${invitationId}`);
  }

  listJoinRequests(
    tenantSlug: string,
    status?: JoinRequestStatus,
  ): Observable<JoinRequestServiceModel[]> {
    return this.http.get<JoinRequestServiceModel[]>(`${this.base(tenantSlug)}/join-requests`, {
      params: status ? { status } : {},
    });
  }

  // The role is in the body because approving IS a role grant, under the same ceiling an invitation
  // has: an officer admits somebody as Member or Officer, only an Owner admits one straight in as Owner.
  approveJoinRequest(tenantSlug: string, requestId: string, role: TenantRole): Observable<void> {
    return this.http.post<void>(`${this.base(tenantSlug)}/join-requests/${requestId}/approve`, {
      role,
    });
  }

  declineJoinRequest(tenantSlug: string, requestId: string): Observable<void> {
    return this.http.post<void>(
      `${this.base(tenantSlug)}/join-requests/${requestId}/decline`,
      null,
    );
  }

  private base(tenantSlug: string): string {
    return `${this.runtimeConfig.gatewayUrl()}/api/v1/t/${tenantSlug}`;
  }
}
