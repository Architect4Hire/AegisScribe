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
}

// Mirrors `CreateTenantViewModel`, the body of POST /api/v1/tenants. `slug` is optional (2.7b): omit
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
