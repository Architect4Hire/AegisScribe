import { ComponentFixture, TestBed } from '@angular/core/testing';
import { EmptyState } from './empty-state';

describe('EmptyState', () => {
  let fixture: ComponentFixture<EmptyState>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [EmptyState],
    }).compileComponents();

    fixture = TestBed.createComponent(EmptyState);
  });

  it('renders heading and body, defaulting to the empty variant with no action', () => {
    fixture.componentRef.setInput('heading', 'No character called "Thornwake" on Argent Dawn');
    fixture.componentRef.setInput(
      'body',
      'Names are per-realm, and a character that transferred keeps its name on the new realm only.',
    );
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('h4')?.textContent).toContain('Thornwake');
    expect(host.querySelector('.state-box')?.classList.contains('is-error')).toBe(false);
    expect(host.querySelector('button')).toBeNull();
  });

  it('renders the action button when a label is given, and emits action on click', () => {
    fixture.componentRef.setInput('heading', 'No results');
    fixture.componentRef.setInput('body', 'Try a different realm.');
    fixture.componentRef.setInput('actionLabel', 'Search all realms');
    fixture.detectChanges();

    let actionCount = 0;
    fixture.componentInstance.action.subscribe(() => actionCount++);

    const host = fixture.nativeElement as HTMLElement;
    const button = host.querySelector<HTMLButtonElement>('button');
    expect(button?.textContent).toContain('Search all realms');
    button?.click();
    expect(actionCount).toBe(1);
  });

  it('renders the error variant as a designed degraded state, not a blank failure', () => {
    fixture.componentRef.setInput('heading', 'Showing data from 6 days ago');
    fixture.componentRef.setInput('body', "Blizzard's API isn't responding.");
    fixture.componentRef.setInput('actionLabel', 'Try again');
    fixture.componentRef.setInput('variant', 'error');
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('.state-box')?.classList.contains('is-error')).toBe(true);
    expect(host.querySelector('h4')?.textContent).toContain('Showing data from 6 days ago');
  });
});
