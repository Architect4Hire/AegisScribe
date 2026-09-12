import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CurrentUserService } from '../../../core/current-user.service';
import { UserServiceModel } from '../../../models/auth.models';
import { EmptyState } from '../../../shared/empty-state/empty-state';
import { Skeleton } from '../../../shared/skeleton/skeleton';

type LoadState = 'loading' | 'loaded' | 'error';

// Where a 404 on a tenant route lands, and where sign-in lands with no active tenant yet. Lists
// every community the caller belongs to; picking one navigates -- see
// .claude/rules/frontend.md -> "Tenant context is part of the chrome".
@Component({
  imports: [EmptyState, RouterLink, Skeleton],
  selector: 'scribe-tenant-picker',
  styleUrl: './tenant-picker.css',
  templateUrl: './tenant-picker.html',
})
export class TenantPicker {
  private readonly currentUser = inject(CurrentUserService);

  readonly state = signal<LoadState>('loading');
  private readonly user = signal<UserServiceModel | null>(null);

  readonly memberships = computed(() => this.user()?.memberships ?? []);

  constructor() {
    void this.load();
  }

  retry(): void {
    this.state.set('loading');
    void this.load();
  }

  private async load(): Promise<void> {
    try {
      const user = await this.currentUser.ensureLoaded();
      this.user.set(user);
      this.state.set('loaded');
    } catch {
      // A 401 here already triggered the interceptor's sign-in redirect; anything else is a
      // genuine failure worth a retry button.
      this.state.set('error');
    }
  }
}
