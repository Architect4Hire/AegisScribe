import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { UserServiceModel } from '../../../models/auth.models';
import { CurrentUserService } from '../../../core/current-user.service';
import { TenantShell } from './tenant-shell';

const user: UserServiceModel = {
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
    {
      tenantId: 't2',
      tenantSlug: 'emberwatch',
      tenantName: 'Emberwatch',
      role: 'Member',
      joinedAt: '2026-02-01T00:00:00Z',
    },
  ],
};

describe('TenantShell', () => {
  let fixture: ComponentFixture<TenantShell>;
  let navigateSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TenantShell],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: CurrentUserService, useValue: { user: () => user, ensureLoaded: () => Promise.resolve(user) } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(TenantShell);
    fixture.componentRef.setInput('tenantSlug', 'ashes-of-dawn');

    const router = TestBed.inject(Router);
    navigateSpy = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    fixture.detectChanges();
  });

  it('builds every nav link from the bound tenantSlug', () => {
    const hrefs = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLAnchorElement>('.appnav a'),
    ).map((a) => a.getAttribute('href'));

    expect(hrefs).toEqual([
      '/t/ashes-of-dawn/roster',
      '/t/ashes-of-dawn/calendar',
      '/t/ashes-of-dawn/search',
      '/t/ashes-of-dawn/advisor',
    ]);
  });

  it('passes the active tenant and memberships down to the switcher', () => {
    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('.tenant-name')?.textContent).toContain('Ashes of Dawn');
    expect(host.querySelector('.tenant-role')?.textContent).toContain('Officer');
  });

  it("navigates on the switcher's switched event, rather than mutating hidden state", () => {
    const host = fixture.nativeElement as HTMLElement;
    host.querySelector<HTMLButtonElement>('.tenant-switch')?.click();
    fixture.detectChanges();
    host.querySelector<HTMLButtonElement>('.tenant-switch-item')?.click();
    fixture.detectChanges();

    expect(navigateSpy).toHaveBeenCalledWith(['/t', 'emberwatch']);
  });
});
