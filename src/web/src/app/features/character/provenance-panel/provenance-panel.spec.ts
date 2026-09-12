import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ProvenancePanel } from './provenance-panel';

describe('ProvenancePanel', () => {
  let fixture: ComponentFixture<ProvenancePanel>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ProvenancePanel],
    }).compileComponents();

    fixture = TestBed.createComponent(ProvenancePanel);
    fixture.componentRef.setInput('lastSyncedAt', '2026-09-02T07:41:00Z');
    fixture.componentRef.setInput('region', 'eu');
    fixture.componentRef.setInput('characterId', 'a1b2c3d4-0000-0000-0000-000000000000');
    fixture.detectChanges();
  });

  it('shows the real last-synced value', () => {
    expect((fixture.nativeElement as HTMLElement).querySelector('dd')?.textContent).toContain(
      '2026-09-02T07:41:00Z',
    );
  });

  it('computes refresh-due as 30 days after the last sync, per the Blizzard refresh obligation', () => {
    const host = fixture.nativeElement as HTMLElement;
    const dds = Array.from(host.querySelectorAll('dd'));
    expect(dds[1].textContent).toContain('2026-10-02');
  });

  it('derives the source from the real region, using the documented profile-{region} namespace shape', () => {
    const host = fixture.nativeElement as HTMLElement;
    const dds = Array.from(host.querySelectorAll('dd'));
    expect(dds[2].textContent).toContain('profile-eu');
  });

  it('shows the real character id', () => {
    const host = fixture.nativeElement as HTMLElement;
    const dds = Array.from(host.querySelectorAll('dd'));
    expect(dds[3].textContent).toContain('a1b2c3d4-0000-0000-0000-000000000000');
  });
});
