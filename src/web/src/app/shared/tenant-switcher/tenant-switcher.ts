import { Component, ElementRef, computed, inject, input, output, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TENANT_PICKER_PATH } from '../../core/tenant.guard';
import { TenantMembershipServiceModel } from '../../models/auth.models';

// Active community + the viewer's role in it. Switching navigates -- it never mutates hidden
// state, because a stored "current tenant" and the URL will drift, and when they do the user is
// looking at one community's data inside another's chrome. See .claude/rules/frontend.md ->
// "Tenant context is part of the chrome".
@Component({
  imports: [RouterLink],
  selector: 'scribe-tenant-switcher',
  styleUrl: './tenant-switcher.css',
  templateUrl: './tenant-switcher.html',
})
export class TenantSwitcher {
  private readonly elementRef = inject(ElementRef<HTMLElement>);

  readonly activeTenantSlug = input.required<string>();
  readonly memberships = input.required<TenantMembershipServiceModel[]>();
  readonly switched = output<string>();

  readonly isOpen = signal(false);

  readonly tenantPickerPath = TENANT_PICKER_PATH;

  readonly active = computed(() =>
    this.memberships().find((membership) => membership.tenantSlug === this.activeTenantSlug()),
  );

  readonly crestInitial = computed(() => this.active()?.tenantName.charAt(0).toUpperCase() ?? '?');

  readonly otherMemberships = computed(() =>
    this.memberships().filter((membership) => membership.tenantSlug !== this.activeTenantSlug()),
  );

  toggle(): void {
    this.isOpen.set(!this.isOpen());
  }

  pick(tenantSlug: string): void {
    this.isOpen.set(false);
    this.switched.emit(tenantSlug);
  }

  close(): void {
    this.isOpen.set(false);
  }

  onDocumentClick(event: MouseEvent): void {
    if (this.isOpen() && !this.elementRef.nativeElement.contains(event.target as Node)) {
      this.isOpen.set(false);
    }
  }

  onEscape(): void {
    this.isOpen.set(false);
  }
}
