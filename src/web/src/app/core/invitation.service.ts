import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, throwError } from 'rxjs';
import {
  InvitationAcceptedServiceModel,
  InvitationPreviewServiceModel,
  InvitationRefusal,
} from '../models/auth.models';
import { skipSignInRedirect } from './auth-redirect.context';
import { RuntimeConfigService } from './runtime-config.service';

// Accepting an invitation is tenant-less — the caller is not a member of anything yet, so there is no
// /t/{slug} to resolve into and the token is the only thing naming a community. Through the gateway
// like every other call; the SPA never addresses api.* (.claude/rules/frontend.md).
//
// The token is a bearer secret that grants membership. It is passed in the path and held nowhere: not
// in localStorage, not in sessionStorage, not in a cookie this app sets, and never logged. It lives in
// the route while the screen is open and in the URL that survives the sign-in round-trip, and nowhere
// else.
@Injectable({ providedIn: 'root' })
export class InvitationService {
  private readonly http = inject(HttpClient);
  private readonly runtimeConfig = inject(RuntimeConfigService);

  // A POST for what is really a read, and the token in the BODY rather than the URL. ASP.NET Core
  // stamps RequestPath onto every log entry a request writes, so a token in the path would land in
  // the server's whole log pipeline; a body lands in none of it. The same reason it is not a query
  // parameter.
  //
  // skipSignInRedirect, because the preview is ANONYMOUS and a 401 here would otherwise bounce a
  // signed-out invitee into the OAuth flow before they had seen what they were invited to — which is
  // the whole reason this endpoint does not require a session.
  preview(token: string): Observable<InvitationPreviewServiceModel> {
    return this.http
      .post<InvitationPreviewServiceModel>(
        `${this.url()}/preview`,
        { token },
        { context: skipSignInRedirect() },
      )
      .pipe(catchError((error: unknown) => throwError(() => refusalOf(error))));
  }

  accept(token: string): Observable<InvitationAcceptedServiceModel> {
    return this.http
      .post<InvitationAcceptedServiceModel>(`${this.url()}/accept`, { token })
      .pipe(catchError((error: unknown) => throwError(() => refusalOf(error))));
  }

  private url(): string {
    return `${this.runtimeConfig.gatewayUrl()}/api/v1/invitations`;
  }
}

// Maps the server's problem `type` onto the four answers this screen renders. Branching on `type`
// rather than on prose or on the status alone is what api-contract.md requires, and it is what lets
// three different 410s say three different things.
export function refusalOf(error: unknown): InvitationRefusal {
  if (!(error instanceof HttpErrorResponse)) {
    return 'unavailable';
  }

  if (error.status === 404) {
    return 'not-found';
  }

  const type: unknown = error.error?.type;

  if (typeof type === 'string') {
    if (type.endsWith('/invitation-expired')) return 'expired';
    if (type.endsWith('/invitation-consumed')) return 'consumed';
    if (type.endsWith('/invitation-revoked')) return 'revoked';
  }

  // A 410 whose type this build does not recognise still means the invitation is dead — clients must
  // tolerate what they do not know (api-contract.md), and the honest fallback is the least specific
  // dead answer rather than pretending the link is live.
  return error.status === 410 ? 'consumed' : 'unavailable';
}
