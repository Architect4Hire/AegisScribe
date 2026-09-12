import { Routes } from '@angular/router';
import { authGuard } from './core/auth.guard';
import { tenantGuard } from './core/tenant.guard';

export const routes: Routes = [
  {
    path: 'tenants',
    canActivate: [authGuard],
    loadComponent: () => import('./features/tenancy/tenant-picker/tenant-picker').then((m) => m.TenantPicker),
  },
  {
    path: 't/:tenantSlug',
    canActivate: [authGuard, tenantGuard],
    loadComponent: () => import('./features/tenancy/tenant-shell/tenant-shell').then((m) => m.TenantShell),
    children: [
      {
        path: 'characters/:region/:realmSlug/:name',
        loadComponent: () =>
          import('./features/character/character-profile/character-profile').then((m) => m.CharacterProfile),
      },
    ],
  },
];
