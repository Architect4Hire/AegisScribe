import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FilterChip } from './filter-chip';

describe('FilterChip', () => {
  let fixture: ComponentFixture<FilterChip>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [FilterChip],
    }).compileComponents();

    fixture = TestBed.createComponent(FilterChip);
    fixture.componentRef.setInput('key', 'ilvl >');
    fixture.componentRef.setInput('value', '620');
    fixture.detectChanges();
  });

  it('renders the key and value', () => {
    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('.chip-key')?.textContent).toContain('ilvl >');
    expect(host.textContent).toContain('620');
  });

  it('labels the remove control with the specific filter it removes', () => {
    const button = (fixture.nativeElement as HTMLElement).querySelector('.chip-x');
    expect(button?.getAttribute('aria-label')).toBe('Remove ilvl > filter');
  });

  it('emits removed when the remove control is clicked', () => {
    let removedCount = 0;
    fixture.componentInstance.removed.subscribe(() => removedCount++);

    (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('.chip-x')?.click();

    expect(removedCount).toBe(1);
  });
});
