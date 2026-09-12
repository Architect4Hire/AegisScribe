import { ComponentFixture, TestBed } from '@angular/core/testing';
import { EquippedItemServiceModel } from '../../models/item.models';
import { EquipmentRail } from './equipment-rail';

const helm: EquippedItemServiceModel = {
  slot: 'Head',
  blizzardItemId: 1,
  itemName: 'Visage of the Emberwrought',
  quality: 'Epic',
  itemLevel: 642,
  iconUrl: 'https://render.worldofwarcraft.com/icons/56/inv_helm_01.jpg',
};

describe('EquipmentRail', () => {
  let fixture: ComponentFixture<EquipmentRail>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [EquipmentRail],
    }).compileComponents();

    fixture = TestBed.createComponent(EquipmentRail);
  });

  it('renders one item-cell per slot, in order, filling in real equipment by slot', () => {
    fixture.componentRef.setInput('slots', ['Head', 'Neck', 'Shoulder']);
    fixture.componentRef.setInput('equipment', [helm]);
    fixture.componentRef.setInput('side', 'left');
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    const cells = host.querySelectorAll('scribe-item-cell');
    expect(cells.length).toBe(3);
    expect(cells[0].querySelector('.item-name')?.textContent).toContain('Visage of the Emberwrought');
  });

  it('renders an empty slot when no equipment entry matches it', () => {
    fixture.componentRef.setInput('slots', ['Head', 'Neck']);
    fixture.componentRef.setInput('equipment', [helm]);
    fixture.componentRef.setInput('side', 'left');
    fixture.detectChanges();

    const cells = (fixture.nativeElement as HTMLElement).querySelectorAll('scribe-item-cell');
    expect(cells[1].querySelector('.item.is-empty')).not.toBeNull();
  });

  it('labels each side for assistive tech', () => {
    fixture.componentRef.setInput('slots', ['Head']);
    fixture.componentRef.setInput('equipment', []);
    fixture.componentRef.setInput('side', 'right');
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('.slot-rail')?.getAttribute('aria-label')).toBe(
      'Right equipment',
    );
  });
});
