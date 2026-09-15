import { ComponentFixture, TestBed } from '@angular/core/testing';
import { RankPill } from './rank-pill';

describe('RankPill', () => {
  let fixture: ComponentFixture<RankPill>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [RankPill],
    }).compileComponents();

    fixture = TestBed.createComponent(RankPill);
  });

  it('renders the name and sets the tenant colour as --rank-color', () => {
    fixture.componentRef.setInput('name', 'Raider');
    fixture.componentRef.setInput('colour', '#cba76a');
    fixture.detectChanges();

    const pill = (fixture.nativeElement as HTMLElement).querySelector('.rank') as HTMLElement;
    expect(pill.textContent).toContain('Raider');
    // A custom property, not a hard-coded colour: the value is the community's config, and the
    // stylesheet is what decides where it lands.
    expect(pill.style.getPropertyValue('--rank-color')).toBe('#cba76a');
  });

  it('falls back to the token default when the rank has no colour', () => {
    fixture.componentRef.setInput('name', 'Trial');
    fixture.componentRef.setInput('colour', null);
    fixture.detectChanges();

    const pill = (fixture.nativeElement as HTMLElement).querySelector('.rank') as HTMLElement;
    expect(pill.style.getPropertyValue('--rank-color')).toBe('');
    // Still a rank, not a broken pill.
    expect(pill.textContent).toContain('Trial');
  });

  it('never carries colour as the only signal — the name is always rendered', () => {
    fixture.componentRef.setInput('name', 'Officer');
    fixture.componentRef.setInput('colour', '#3d9bff');
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent?.trim()).toContain('Officer');
  });
});
