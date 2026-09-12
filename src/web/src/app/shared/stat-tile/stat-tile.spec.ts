import { ComponentFixture, TestBed } from '@angular/core/testing';
import { StatTile } from './stat-tile';

describe('StatTile', () => {
  let fixture: ComponentFixture<StatTile>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [StatTile],
    }).compileComponents();

    fixture = TestBed.createComponent(StatTile);
  });

  it('renders label and value', () => {
    fixture.componentRef.setInput('label', 'Item level');
    fixture.componentRef.setInput('value', 639);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('.tile-label')?.textContent).toContain('Item level');
    expect(host.querySelector('.tile-value')?.textContent).toContain('639');
  });

  it('omits the sub-line when none is given', () => {
    fixture.componentRef.setInput('label', 'Mythic+');
    fixture.componentRef.setInput('value', '2 841');
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('.tile-sub')).toBeNull();
  });

  it('renders the sub-line when given', () => {
    fixture.componentRef.setInput('label', 'Item level');
    fixture.componentRef.setInput('value', 639);
    fixture.componentRef.setInput('sub', '+4 this week');
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('.tile-sub')?.textContent).toContain(
      '+4 this week',
    );
  });
});
