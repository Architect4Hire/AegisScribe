import { Routes } from '@angular/router';
import { authGuard } from './core/auth.guard';
import { landingGuard } from './core/landing.guard';
import { tenantGuard } from './core/tenant.guard';

export const routes: Routes = [
  {
    // The app's first route, and the gateway's post-sign-in landing pad. landingGuard decides
    // whether '/' is the signed-out page or a redirect to the tenant picker.
    path: '',
    pathMatch: 'full',
    canActivate: [landingGuard],
    loadComponent: () => import('./features/account/landing/landing').then((m) => m.Landing),
  },
  {
    // Ahead of 'tenants' so the more specific path matches first. Deliberately NOT '/t/new': that
    // would sit inside the tenant namespace and depend on the reserved-slug list to stay safe, whereas
    // here it cannot collide with a community's slug at all.
    path: 'tenants/new',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/tenancy/create-community/create-community').then((m) => m.CreateCommunity),
  },
  {
    path: 'tenants',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/tenancy/tenant-picker/tenant-picker').then((m) => m.TenantPicker),
  },
  {
    // The public front door, deliberately unguarded: character data is GLOBAL reference data and the
    // API behind it is anonymous by design, so a character page must render for a visitor with no
    // session and no community.
    //
    // The SAME component serves this and the in-tenant route below, taking tenantSlug as an optional
    // input and showing no claim state when there isn't one. Outside the tenant shell, so there is no
    // appbar and no tenant switcher — both are tenant chrome. The Blizzard attribution lives in
    // app.html outside the router outlet, so this route carries it like every other.
    path: 'characters/:region/:realmSlug/:name',
    loadComponent: () =>
      import('./features/character/character-profile/character-profile').then(
        (m) => m.CharacterProfile,
      ),
  },
  {
    path: 't/:tenantSlug',
    canActivate: [authGuard, tenantGuard],
    loadComponent: () =>
      import('./features/tenancy/tenant-shell/tenant-shell').then((m) => m.TenantShell),
    children: [
      {
        path: 'roster',
        loadComponent: () =>
          import('./features/roster/roster-table/roster-table').then((m) => m.RosterTable),
      },
      {
        // Officer-facing, but NOT guarded here: hiding a route is cosmetics, and the APIs behind this
        // screen carry TenantOfficer policies of their own. A member who types the URL sees the ladder
        // read-only and every write refused (auth.md).
        path: 'ranks',
        loadComponent: () =>
          import('./features/guild/rank-manager/rank-manager').then((m) => m.RankManager),
      },
      {
        path: 'characters/:region/:realmSlug/:name',
        loadComponent: () =>
          import('./features/character/character-profile/character-profile').then(
            (m) => m.CharacterProfile,
          ),
      },
    ],
  },
];
