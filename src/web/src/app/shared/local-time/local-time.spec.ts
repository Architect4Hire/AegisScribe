import { ComponentFixture, TestBed } from '@angular/core/testing';
import { LocalTime } from './local-time';

describe('LocalTime', () => {
  let fixture: ComponentFixture<LocalTime>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [LocalTime],
    }).compileComponents();

    fixture = TestBed.createComponent(LocalTime);
  });

  it('renders the tenant zone by default, labelled with its offset', () => {
    fixture.componentRef.setInput('instant', '2026-09-01T18:00:00Z');
    fixture.componentRef.setInput('tenantZone', 'Europe/Berlin');
    fixture.componentRef.setInput('viewerZone', 'America/New_York');
    fixture.detectChanges();

    const time = (fixture.nativeElement as HTMLElement).querySelector('time');
    // 18:00 UTC = 20:00 in Europe/Berlin (CEST, UTC+2) in September.
    expect(time?.textContent).toContain('20:00');
    expect(time?.textContent).toMatch(/GMT\+2|CEST/);
  });

  it('carries the machine-readable instant separately from the display label', () => {
    fixture.componentRef.setInput('instant', '2026-09-01T18:00:00Z');
    fixture.componentRef.setInput('tenantZone', 'Europe/Berlin');
    fixture.detectChanges();

    expect(
      (fixture.nativeElement as HTMLElement).querySelector('time')?.getAttribute('datetime'),
    ).toBe('2026-09-01T18:00:00Z');
  });

  it('exposes the viewer zone on hover, labelled as such and distinct from the tenant time', () => {
    fixture.componentRef.setInput('instant', '2026-09-01T18:00:00Z');
    fixture.componentRef.setInput('tenantZone', 'Europe/Berlin');
    fixture.componentRef.setInput('viewerZone', 'America/New_York');
    fixture.detectChanges();

    const time = (fixture.nativeElement as HTMLElement).querySelector('time');
    const title = time?.getAttribute('title') ?? '';
    // 18:00 UTC = 14:00 in America/New_York (EDT, UTC-4) in September.
    expect(title).toContain('14:00');
    expect(title).toContain('your time');
    expect(title).not.toBe(time?.textContent?.trim());
  });
});
