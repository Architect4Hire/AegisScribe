import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Skeleton } from './skeleton';

describe('Skeleton', () => {
  let fixture: ComponentFixture<Skeleton>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Skeleton],
    }).compileComponents();

    fixture = TestBed.createComponent(Skeleton);
  });

  it('renders a single shape-matched bar by default, never a spinner', () => {
    fixture.componentRef.setInput('height', 34);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    const bars = host.querySelectorAll<HTMLElement>('.sk');
    expect(bars.length).toBe(1);
    expect(bars[0].style.height).toBe('34px');
    expect(host.querySelector('[class*="spinner"]')).toBeNull();
  });

  it('renders `count` bars, fading further down the stack', () => {
    fixture.componentRef.setInput('height', 34);
    fixture.componentRef.setInput('count', 3);
    fixture.detectChanges();

    const bars = (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('.sk');
    expect(bars.length).toBe(3);
    expect(bars[0].style.opacity).toBe('1');
    expect(bars[1].style.opacity).toBe('0.75');
    expect(bars[2].style.opacity).toBe('0.5');
  });

  it('floors opacity at 0.5 rather than fading to invisible for long stacks', () => {
    fixture.componentRef.setInput('height', 20);
    fixture.componentRef.setInput('count', 8);
    fixture.detectChanges();

    const bars = (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('.sk');
    expect(bars[bars.length - 1].style.opacity).toBe('0.5');
  });
});
