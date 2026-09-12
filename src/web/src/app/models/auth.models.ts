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
