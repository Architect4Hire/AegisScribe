import { CharacterClass } from './character.models';

// Mirrors `AegisScribe.Domain.Managers.Models.Domain.TenantRole`. Enums serialize as their name
// (Program.cs registers a JsonStringEnumConverter for both Mvc and minimal-API JSON options).
export type TenantRole = 'Member' | 'Officer' | 'Owner';

// Mirrors `TenantMembershipServiceModel`.
export interface TenantMembershipServiceModel {
  tenantId: string;
  tenantSlug: string;
  tenantName: string;
  role: TenantRole;
  joinedAt: string;
}

// Mirrors `UserServiceModel`, returned by `GET /api/v1/me`.
export interface UserServiceModel {
  id: string;
  email: string;
  displayName: string | null;
  createdAt: string;
  memberships: TenantMembershipServiceModel[];
}

// Mirrors `RegisterViewModel`, the body of POST /api/v1/auth/register. The response is a
// `UserServiceModel`, and it is NOT a session: creating an account does not sign you in, and the
// SPA holds no tokens in any case (.claude/rules/auth.md -> "Angular side").
export interface RegisterViewModel {
  email: string;
  password: string;
  displayName?: string | null;
}

// Mirrors `TenantServiceModel`, returned by POST /api/v1/tenants and GET /api/v1/t/{slug}.
export interface TenantServiceModel {
  id: string;
  slug: string;
  name: string;
  timeZoneId: string;

  // Whether the community's "ask to join" door is open. Only ever populated for an Owner, because the
  // route that returns this model is Owner-only — an Officer reading it gets a 403, which is why the
  // member screen's request queue says nothing about the setting.
  acceptsJoinRequests: boolean;
}

// Mirrors `CreateTenantViewModel`, the body of POST /api/v1/tenants. `slug` is optional: omit
// it and the server derives one from `name` via TenantSlugRules. The derivation is deliberately NOT
// reimplemented here — the create screen renders whatever GET /tenants/slug-check answers.
export interface CreateTenantViewModel {
  name: string;
  timeZoneId: string;
  slug?: string | null;
}

// Mirrors `SlugCheckReason`. Enums serialize as their name, so this is a string union — and it must
// tolerate a value added after this build shipped (api-contract.md), which is what `available`
// existing alongside it is for: branch on the boolean, render the reason.
export type SlugCheckReason = 'Available' | 'Taken' | 'Reserved' | 'Invalid' | 'NotDerivable';

// Mirrors `SlugCheckServiceModel`, returned by GET /api/v1/tenants/slug-check.
export interface SlugCheckServiceModel {
  slug: string | null;
  available: boolean;
  reason: SlugCheckReason;
}

// Mirrors `TenantOverviewServiceModel`, returned by GET /api/v1/t/{slug}/overview (TenantOfficer).
//
// Five facts about what a community HAS — never a step number. The first-run checklist derives its
// state from these, so deleting a rank correctly un-completes that step; a server-side progress marker
// would start lying the first time an officer changed their mind.
//
// `blizzardConfigured` is a fact about the DEPLOYMENT, not the community: credentials are optional by
// design and the gateway no-ops without them, so the guild step says so plainly rather than failing.
export interface TenantOverviewServiceModel {
  hasLinkedGuild: boolean;
  rankCount: number;
  rosterCount: number;
  // The caller included, so a brand-new community reads 1, never 0.
  memberCount: number;
  blizzardConfigured: boolean;
}

// The invitee's half of the invitation surface (8.3b). The officer's half is at the foot of this file.
//
// Mirrors `InvitationPreviewServiceModel`, returned by GET /api/v1/invitations/{token}. It exists
// ONLY for a live token — every refusal comes back as a problem response carrying none of this,
// because naming the community to somebody holding a dead token tells them something the token no
// longer entitles them to.
export interface InvitationPreviewServiceModel {
  tenantSlug: string;
  tenantName: string;
  role: TenantRole;
  expiresAt: string;
}

// Mirrors `InvitationAcceptedServiceModel`, returned by POST /api/v1/invitations/{token}/accept.
// `role` is their role NOW — for somebody who was already a member that is the role they already had,
// since accepting never changes a standing role in either direction.
export interface InvitationAcceptedServiceModel {
  tenantSlug: string;
  tenantName: string;
  role: TenantRole;
  alreadyAMember: boolean;
}

// The three ways a real invitation can be dead, told apart by the problem `type` (api-contract.md:
// clients branch on type, never on prose). 'not-found' is the fourth answer and deliberately does not
// mean the same thing — a token that never existed reveals nothing at all.
export type InvitationRefusal = 'expired' | 'consumed' | 'revoked' | 'not-found' | 'unavailable';

// ── The officer's half of the membership lifecycle (8.3) ────────────────────────────────────────
//
// Beside the invitee's half above rather than in a file of their own: these are the same vertical seen
// from the other side, and TenantRole already lives here.

// Mirrors `MemberClaimedCharacterServiceModel`. `region` + `realmSlug` + `name` are the three segments
// the character route takes, so each of these renders as a link rather than dead text.
export interface MemberClaimedCharacterServiceModel {
  characterId: string;
  name: string;
  realmSlug: string;
  region: string;
  class: CharacterClass;
  // Mapped API-side. The frontend is forbidden a class→hex table of its own (frontend.md).
  classColor: string;
}

// Mirrors `TenantMemberServiceModel`, an item of the page returned by GET /t/{slug}/members.
//
// `displayName` is null for somebody who never set one — the UI falls back rather than showing an
// email address, which is not a fellow member's to see. `claimedCharacters` is `[]`, never null, for a
// member who has claimed nothing: that is the answer the officer screen is asking for, not an absence.
export interface TenantMemberServiceModel {
  userId: string;
  displayName: string | null;
  role: TenantRole;
  joinedAt: string;
  claimedCharacters: MemberClaimedCharacterServiceModel[];
}

// Mirrors `InvitationStatus`. A string union rather than a lookup the UI depends on being exhaustive:
// a value added after this build shipped must not break us (api-contract.md).
export type InvitationStatus = 'Pending' | 'Accepted' | 'Revoked' | 'Expired';

// Mirrors `InvitationServiceModel`. There is no token on this type and that absence is the design —
// the plaintext exists once, on the create response below, and is never readable again.
export interface InvitationServiceModel {
  id: string;
  role: TenantRole;
  note: string | null;
  createdAt: string;
  expiresAt: string;
  status: InvitationStatus;
}

// Mirrors `CreatedInvitationServiceModel`, returned by POST /t/{slug}/invitations. The only time the
// token is ever on the wire outbound: show it once, hold it nowhere, and do not re-request it.
export interface CreatedInvitationServiceModel {
  invitation: InvitationServiceModel;
  token: string;
}

// Mirrors `CreateInvitationViewModel`. No invitee identity — we do not send these (email is out of
// bounds), so an officer copies the link and passes it on themselves; `note` is how they tell two
// outstanding links apart. `expiresInDays` omitted lets the server pick its default.
export interface CreateInvitationViewModel {
  role: TenantRole;
  note?: string | null;
  expiresInDays?: number | null;
}

// Mirrors `JoinRequestStatus`. String union for the same reason as InvitationStatus.
export type JoinRequestStatus = 'Pending' | 'Approved' | 'Declined';

// Mirrors `JoinRequestServiceModel`. `message` is the applicant's own words, written by somebody
// outside the community — untrusted text wherever it lands, so it is rendered as text and never as
// markup, and it never reaches a prompt unfenced (ai.md).
export interface JoinRequestServiceModel {
  id: string;
  userId: string;
  displayName: string | null;
  message: string | null;
  status: JoinRequestStatus;
  requestedAt: string;
  decidedAt: string | null;
}

// Mirrors `SetMemberRoleViewModel`. The target is the userId in the route; this carries only what they
// become. Whether the caller may grant it is the server's to decide.
export interface SetMemberRoleViewModel {
  role: TenantRole;
}
