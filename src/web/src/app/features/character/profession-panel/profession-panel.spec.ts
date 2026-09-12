import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ProfessionPanel } from './profession-panel';

describe('ProfessionPanel', () => {
  let fixture: ComponentFixture<ProfessionPanel>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ProfessionPanel],
    }).compileComponents();

    fixture = TestBed.createComponent(ProfessionPanel);
  });

  it('shows an honest empty state when there is no profession data, rather than a blank panel', () => {
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('scribe-empty-state')?.textContent).toContain('No profession data yet');
    expect(host.querySelector('.prof')).toBeNull();
  });

  it('renders one row per profession when data is present', () => {
    fixture.componentRef.setInput('rows', [{ name: 'Blacksmithing', skillLevel: 88, maxSkillLevel: 100 }]);
    fixture.detectChanges();

    const row = (fixture.nativeElement as HTMLElement).querySelector('.prof');
    expect(row?.textContent).toContain('Blacksmithing');
    expect(row?.textContent).toContain('88/100');
  });
});
