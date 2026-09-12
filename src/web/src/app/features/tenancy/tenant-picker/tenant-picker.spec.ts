import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { CurrentUserService } from '../../../core/current-user.service';
import { UserServiceModel } from '../../../models/auth.models';
import { TenantPicker } from './tenant-picker';

const userWithMemberships: UserServiceModel = {
  id: 'u1',
  email: 'a@b.com',
  displayName: null,
  createdAt: '2026-01-01T00:00:00Z',
  memberships: [
    {
      tenantId: 't1',
      tenantSlug: 'ashes-of-dawn',
      tenantName: 'Ashes of Dawn',
      role: 'Officer',
      joinedAt: '2026-01-01T00:00:00Z',
    },
  ],
};

const userWithNoMemberships: UserServiceModel = { ...userWithMemberships, memberships: [] };

async function createWith(
  ensureLoaded: () => Promise<UserServiceModel>,
): Promise<ComponentFixture<TenantPicker>> {
  await TestBed.configureTestingModule({
    imports: [TenantPicker],
    providers: [provideRouter([]), { provide: CurrentUserService, useValue: { ensureLoaded } }],
  }).compileComponents();

  return TestBed.createComponent(TenantPicker);
}

describe('TenantPicker', () => {
  it('shows a shape-matched skeleton while loading', async () => {
    const fixture = await createWith(() => new Promise(() => {}));
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('scribe-skeleton')).not.toBeNull();
  });

  it('lists every membership once loaded, each linking to its tenant route', async () => {
    const fixture = await createWith(() => Promise.resolve(userWithMemberships));
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    const link = host.querySelector<HTMLAnchorElement>('.tenant-card');
    expect(link?.textContent).toContain('Ashes of Dawn');
    expect(link?.textContent).toContain('Officer');
    expect(link?.getAttribute('href')).toBe('/t/ashes-of-dawn');
  });

  it('shows an empty state for a caller with no memberships, not an error', async () => {
    const fixture = await createWith(() => Promise.resolve(userWithNoMemberships));
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('.tenant-card')).toBeNull();
    expect(host.querySelector('scribe-empty-state')?.textContent).toContain('not in any communities');
  });

  it('shows a retryable error state when loading fails for a reason other than a session timeout', async () => {
    const fixture = await createWith(() => Promise.reject(new Error('network down')));
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    const emptyState = host.querySelector('scribe-empty-state');
    expect(emptyState?.textContent).toContain("Couldn't load");
    expect(emptyState?.querySelector('button')).not.toBeNull();
  });
});
