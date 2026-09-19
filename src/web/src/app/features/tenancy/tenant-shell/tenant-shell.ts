import { Component, computed, inject, input } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { CurrentUserService } from '../../../core/current-user.service';
import { TENANT_PICKER_PATH } from '../../../core/tenant.guard';
import { TenantSwitcher } from '../../../shared/tenant-switcher/tenant-switcher';

interface NavLink {
  label: string;
  path: string;
}

// The chrome for every /t/:tenantSlug/... route. tenantSlug comes from the router
// (withComponentInputBinding in app.config.ts) on every navigation -- never cached past that, so
// services this shell's children build never drift from what the URL actually says. See
// .claude/rules/frontend.md -> "Tenant context is part of the chrome".
@Component({
  imports: [RouterLink, RouterLinkActive, RouterOutlet, TenantSwitcher],
  selector: 'scribe-tenant-shell',
  styleUrl: './tenant-shell.css',
  templateUrl: './tenant-shell.html',
})
export class TenantShell {
  private readonly router = inject(Router);
  private readonly currentUser = inject(CurrentUserService);

  readonly tenantSlug = input.required<string>();

  // The brand is the way out of the active community -- to the picker, where creating another one
  // lives too.
  readonly tenantPickerPath = TENANT_PICKER_PATH;

  // Some of these point at features that don't exist yet -- inert until they land, same as
  // TENANT_PICKER_PATH was inert from 5.5 until this task registered it.
  //
  // 'Members' is here for everybody, not just officers: the member list is TenantMember, so a plain
  // member seeing who is in their community is the intended behaviour. What an officer additionally
  // sees on that screen is decided on the screen, by the policies behind it.
  readonly navLinks = computed<NavLink[]>(() => {
    const base = `/t/${this.tenantSlug()}`;
    return [
      { label: 'Roster', path: `${base}/roster` },
      { label: 'Guilds', path: `${base}/guilds` },
      { label: 'Members', path: `${base}/members` },
      { label: 'Calendar', path: `${base}/calendar` },
      { label: 'Armory', path: `${base}/search` },
      { label: 'Advisor', path: `${base}/advisor` },
    ];
  });

  readonly memberships = computed(() => this.currentUser.user()?.memberships ?? []);

  constructor() {
    // Defensive, not load-bearing: tenantGuard already called ensureLoaded() before this
    // component could activate. Cheap because the cache makes a repeat call a no-op.
    void this.currentUser.ensureLoaded();
  }

  onSwitched(tenantSlug: string): void {
    void this.router.navigate(['/t', tenantSlug]);
  }
}
