import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ProgressionPanel } from './progression-panel';

describe('ProgressionPanel', () => {
  let fixture: ComponentFixture<ProgressionPanel>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ProgressionPanel],
    }).compileComponents();

    fixture = TestBed.createComponent(ProgressionPanel);
  });

  it('shows an honest empty state when there is no progression data, rather than a blank panel', () => {
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('scribe-empty-state')?.textContent).toContain('No progression data yet');
    expect(host.querySelector('.prog-row')).toBeNull();
  });

  it('renders one row per difficulty when data is present', () => {
    fixture.componentRef.setInput('rows', [
      { difficulty: 'Mythic', killed: 2, total: 9 },
      { difficulty: 'Heroic', killed: 8, total: 9 },
    ]);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    const rows = host.querySelectorAll('.prog-row');
    expect(rows.length).toBe(2);
    expect(rows[0].textContent).toContain('Mythic');
    expect(rows[0].textContent).toContain('2/9');
  });
});
