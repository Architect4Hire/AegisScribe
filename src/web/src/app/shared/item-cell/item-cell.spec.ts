import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ItemCell } from './item-cell';
import { EquippedItemServiceModel } from '../../models/item.models';

function item(overrides: Partial<EquippedItemServiceModel> = {}): EquippedItemServiceModel {
  return {
    slot: 'Waist',
    blizzardItemId: 219471,
    itemName: 'Frayed Emberweave Cinch',
    quality: 'Uncommon',
    itemLevel: 625,
    iconUrl: 'https://render.worldofwarcraft.com/icons/56/inv_belt_web_c_01.jpg',
    ...overrides,
  };
}

describe('ItemCell', () => {
  let fixture: ComponentFixture<ItemCell>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ItemCell],
    }).compileComponents();

    fixture = TestBed.createComponent(ItemCell);
    fixture.componentRef.setInput('slot', 'Waist');
  });

  it('renders an outbound Wowhead anchor built from blizzardItemId', () => {
    fixture.componentRef.setInput('item', item({ blizzardItemId: 219471 }));
    fixture.detectChanges();

    const anchor = (fixture.nativeElement as HTMLElement).querySelector('a.item');
    expect(anchor?.getAttribute('href')).toBe('https://www.wowhead.com/item=219471');
    expect(anchor?.getAttribute('target')).toBe('_blank');
    expect(anchor?.getAttribute('rel')).toBe('noopener noreferrer');
  });

  it('quality sets the left-border class and the name colour class, nothing else', () => {
    fixture.componentRef.setInput('item', item({ quality: 'Legendary' }));
    fixture.detectChanges();

    const anchor = (fixture.nativeElement as HTMLElement).querySelector('a.item');
    expect(anchor?.classList.contains('q-legendary')).toBe(true);
    expect(anchor?.classList.contains('is-flagged')).toBe(false);
  });

  it('a flagged cell tints the background and adds a word, and keeps the quality border class', () => {
    fixture.componentRef.setInput('item', item({ quality: 'Uncommon' }));
    fixture.componentRef.setInput('flagged', true);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    const anchor = host.querySelector('a.item');
    expect(anchor?.classList.contains('is-flagged')).toBe(true);
    expect(anchor?.classList.contains('q-uncommon')).toBe(true);
    expect(host.querySelector('.item-flag')?.textContent).toContain('Weakest');
  });

  it('shows the slot and quality in the meta line by default', () => {
    fixture.componentRef.setInput('slot', 'MainHand');
    fixture.componentRef.setInput('item', item({ slot: 'MainHand', quality: 'Artifact' }));
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('.item-meta')?.textContent).toContain(
      'Main hand',
    );
  });

  it('hides the slot in the meta line when showSlot is false', () => {
    fixture.componentRef.setInput('slot', 'Neck');
    fixture.componentRef.setInput('item', item({ slot: 'Neck', quality: 'Rare' }));
    fixture.componentRef.setInput('showSlot', false);
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('.item-meta')?.textContent).not.toContain(
      'Neck',
    );
  });

  it('falls back to no icon image when iconUrl is null, rather than erroring', () => {
    fixture.componentRef.setInput('item', item({ iconUrl: null }));
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('.item-icon img')).toBeNull();
  });

  it('renders the URL the API resolved directly, with no client-side path assembly', () => {
    fixture.componentRef.setInput(
      'item',
      item({ iconUrl: 'https://render.worldofwarcraft.com/icons/56/inv_belt_web_c_01.jpg' }),
    );
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('.item-icon img')?.getAttribute('src')).toBe(
      'https://render.worldofwarcraft.com/icons/56/inv_belt_web_c_01.jpg',
    );
  });

  it('falls back to no icon image once the real one 404s', () => {
    fixture.componentRef.setInput('item', item());
    fixture.detectChanges();

    const img = (fixture.nativeElement as HTMLElement).querySelector('.item-icon img');
    img?.dispatchEvent(new Event('error'));
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('.item-icon img')).toBeNull();
  });

  it('renders an empty slot as a plain div, not a link, labelled with the slot name', () => {
    fixture.componentRef.setInput('slot', 'Trinket2');
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('a.item')).toBeNull();
    const empty = host.querySelector('div.item.is-empty');
    expect(empty).not.toBeNull();
    expect(empty?.querySelector('.item-name')?.textContent).toContain('Empty — Trinket');
    expect(empty?.querySelector('.item-meta')?.textContent).toContain('No item equipped');
    expect(empty?.querySelector('.item-ilvl')?.textContent).toContain('—');
  });

  it('an empty slot never carries a quality or flagged class', () => {
    fixture.componentRef.setInput('slot', 'Head');
    fixture.detectChanges();

    const empty = (fixture.nativeElement as HTMLElement).querySelector('.item');
    expect(empty?.className).not.toMatch(/q-/);
    expect(empty?.classList.contains('is-flagged')).toBe(false);
  });
});
