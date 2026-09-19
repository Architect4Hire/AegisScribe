import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TenantMembershipServiceModel } from '../../models/auth.models';
import { TenantSwitcher } from './tenant-switcher';

const memberships: TenantMembershipServiceModel[] = [
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
];

describe('TenantSwitcher', () => {
  let fixture: ComponentFixture<TenantSwitcher>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TenantSwitcher],
      providers: [provideRouter([])],
    }).compileComponents();

    fixture = TestBed.createComponent(TenantSwitcher);
    fixture.componentRef.setInput('activeTenantSlug', 'ashes-of-dawn');
    fixture.componentRef.setInput('memberships', memberships);
    fixture.detectChanges();
  });

  it("shows the active tenant's name and the viewer's role in it", () => {
    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('.tenant-name')?.textContent).toContain('Ashes of Dawn');
    expect(host.querySelector('.tenant-role')?.textContent).toContain('Officer');
  });

  it('the menu starts closed', () => {
    expect((fixture.nativeElement as HTMLElement).querySelector('.tenant-switch-menu')).toBeNull();
  });

  it('opens to list every OTHER membership, never the active one', () => {
    (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('.tenant-switch')?.click();
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    const items = host.querySelectorAll('.tenant-switch-item');
    expect(items.length).toBe(1);
    expect(items[0].textContent).toContain('Emberwatch');
    expect(host.querySelector('.tenant-switch-menu')?.textContent).not.toContain('Ashes of Dawn');
  });

  it('emits switched with the picked slug, and closes the menu', () => {
    let emitted: string | undefined;
    fixture.componentInstance.switched.subscribe((slug) => (emitted = slug));

    const host = fixture.nativeElement as HTMLElement;
    host.querySelector<HTMLButtonElement>('.tenant-switch')?.click();
    fixture.detectChanges();
    host.querySelector<HTMLButtonElement>('.tenant-switch-item')?.click();
    fixture.detectChanges();

    expect(emitted).toBe('emberwatch');
    expect(host.querySelector('.tenant-switch-menu')).toBeNull();
  });

  it('closes on Escape', () => {
    const host = fixture.nativeElement as HTMLElement;
    host.querySelector<HTMLButtonElement>('.tenant-switch')?.click();
    fixture.detectChanges();
    expect(host.querySelector('.tenant-switch-menu')).not.toBeNull();

    host.querySelector('.tenant-switch')?.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }),
    );
    fixture.detectChanges();

    expect(host.querySelector('.tenant-switch-menu')).toBeNull();
  });

  it('closes on an outside click, but not on a click inside the menu', () => {
    const host = fixture.nativeElement as HTMLElement;
    host.querySelector<HTMLButtonElement>('.tenant-switch')?.click();
    fixture.detectChanges();

    document.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    fixture.detectChanges();

    expect(host.querySelector('.tenant-switch-menu')).toBeNull();
  });

  it('offers every community and creating one, beneath the other memberships', () => {
    const host = fixture.nativeElement as HTMLElement;
    host.querySelector<HTMLButtonElement>('.tenant-switch')?.click();
    fixture.detectChanges();

    const actions = Array.from(host.querySelectorAll<HTMLAnchorElement>('.tenant-switch-action'));
    expect(actions.map((a) => a.getAttribute('href'))).toEqual(['/tenants', '/tenants/new']);
  });

  it('still opens with a single membership, so there is always a way out', () => {
    fixture.componentRef.setInput('memberships', [memberships[0]]);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    host.querySelector<HTMLButtonElement>('.tenant-switch')?.click();
    fixture.detectChanges();

    expect(host.querySelectorAll('.tenant-switch-item').length).toBe(0);
    expect(host.querySelectorAll('.tenant-switch-action').length).toBe(2);
  });

  it('closes when one of its links is followed', () => {
    const host = fixture.nativeElement as HTMLElement;
    host.querySelector<HTMLButtonElement>('.tenant-switch')?.click();
    fixture.detectChanges();
    host.querySelector<HTMLAnchorElement>('.tenant-switch-action')?.click();
    fixture.detectChanges();

    expect(host.querySelector('.tenant-switch-menu')).toBeNull();
  });
});
