import { ComponentFixture, TestBed } from '@angular/core/testing';
import { StatusPill } from './status-pill';

describe('StatusPill', () => {
  let fixture: ComponentFixture<StatusPill>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [StatusPill],
    }).compileComponents();

    fixture = TestBed.createComponent(StatusPill);
  });

  it('renders the label and a dot for a semantic state', () => {
    fixture.componentRef.setInput('state', 'ok');
    fixture.componentRef.setInput('label', 'Synced');
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('.pill')?.classList.contains('pill-ok')).toBe(true);
    expect(host.querySelector('.pill i')).not.toBeNull();
    expect(host.textContent).toContain('Synced');
  });

  it('renders neutral state without a dot', () => {
    fixture.componentRef.setInput('state', 'neutral');
    fixture.componentRef.setInput('label', 'Officer');
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('.pill')?.classList.contains('pill-neutral')).toBe(true);
    expect(host.querySelector('.pill i')).toBeNull();
  });

  it('never carries colour as the only signal — the label text is always present', () => {
    for (const state of ['ok', 'attention', 'critical', 'neutral'] as const) {
      fixture.componentRef.setInput('state', state);
      fixture.componentRef.setInput('label', 'Stale 12d');
      fixture.detectChanges();
      expect((fixture.nativeElement as HTMLElement).textContent?.trim()).toContain('Stale 12d');
    }
  });
});
