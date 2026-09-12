import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Meter } from './meter';

describe('Meter', () => {
  let fixture: ComponentFixture<Meter>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Meter],
    }).compileComponents();

    fixture = TestBed.createComponent(Meter);
  });

  it('fills proportionally to value/max', () => {
    fixture.componentRef.setInput('value', 41);
    fixture.componentRef.setInput('max', 100);
    fixture.detectChanges();

    const fill = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('.meter i');
    expect(fill?.style.width).toBe('41%');
  });

  it('clamps to 100% rather than overflowing', () => {
    fixture.componentRef.setInput('value', 9);
    fixture.componentRef.setInput('max', 9);
    fixture.detectChanges();

    const fill = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('.meter i');
    expect(fill?.style.width).toBe('100%');
  });

  it('never divides by zero when max is 0', () => {
    fixture.componentRef.setInput('value', 0);
    fixture.componentRef.setInput('max', 0);
    fixture.detectChanges();

    const fill = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('.meter i');
    expect(fill?.style.width).toBe('0%');
  });

  it('applies the muted is-partial fill without touching the brass default', () => {
    fixture.componentRef.setInput('value', 41);
    fixture.componentRef.setInput('max', 100);
    fixture.componentRef.setInput('variant', 'partial');
    fixture.detectChanges();

    expect(
      (fixture.nativeElement as HTMLElement).querySelector('.meter i')?.classList.contains('is-partial'),
    ).toBe(true);
  });

  it('exposes progress semantics for assistive tech', () => {
    fixture.componentRef.setInput('value', 8);
    fixture.componentRef.setInput('max', 9);
    fixture.detectChanges();

    const meter = (fixture.nativeElement as HTMLElement).querySelector('.meter');
    expect(meter?.getAttribute('role')).toBe('progressbar');
    expect(meter?.getAttribute('aria-valuenow')).toBe('8');
    expect(meter?.getAttribute('aria-valuemax')).toBe('9');
  });

  it('carries an accessible name when the meter has no adjacent visible label', () => {
    fixture.componentRef.setInput('value', 2);
    fixture.componentRef.setInput('max', 9);
    fixture.componentRef.setInput('label', 'Mythic progress');
    fixture.detectChanges();

    expect(
      (fixture.nativeElement as HTMLElement).querySelector('.meter')?.getAttribute('aria-label'),
    ).toBe('Mythic progress');
  });
});
