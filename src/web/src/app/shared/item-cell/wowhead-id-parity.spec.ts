import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ItemCell } from './item-cell';
import { EquippedItemServiceModel } from '../../models/item.models';

// The whole Wowhead integration rests on one undocumented assumption: that Blizzard's item ids and
// Wowhead's are the same numbers. Nothing guarantees it, and the strategy has held for years, which is
// what makes it dangerous — it is believed rather than checked (external.md).
//
// What this test CAN do is pin the transformation: the id we render must be the Blizzard id, unchanged.
// If somebody ever "fixes" a mismatched link by introducing a translation, this fails and forces the
// conversation rather than letting one special case become a quiet second id space.
//
// It deliberately does NOT fetch anything from Wowhead to confirm the item matches — scraping them to
// prove a link works would be a worse violation than a broken link. Verification is a human opening the
// URLs, which is why these are real, recognisable items.
describe('Wowhead item id parity', () => {
  // Chosen so a human can check them by eye. If Wowhead ever shows something other than the named item
  // at these ids, the id spaces have diverged and the linking strategy — not this test — is wrong.
  const KNOWN_ITEMS: ReadonlyArray<{ readonly blizzardItemId: number; readonly name: string }> = [
    { blizzardItemId: 19019, name: 'Thunderfury, Blessed Blade of the Windseeker' },
    { blizzardItemId: 17182, name: 'Sulfuras, Hand of Ragnaros' },
    { blizzardItemId: 32837, name: 'Warglaive of Azzinoth' },
    { blizzardItemId: 6948, name: 'Hearthstone' },
  ];

  let fixture: ComponentFixture<ItemCell>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ItemCell],
    }).compileComponents();

    fixture = TestBed.createComponent(ItemCell);
    fixture.componentRef.setInput('slot', 'MainHand');
  });

  it('renders the Blizzard item id into the Wowhead URL completely unchanged', () => {
    for (const known of KNOWN_ITEMS) {
      fixture.componentRef.setInput('item', item(known.blizzardItemId));
      fixture.detectChanges();

      const href = (fixture.nativeElement as HTMLElement)
        .querySelector('a.item')
        ?.getAttribute('href');

      // The id itself, not an id derived from it. Any arithmetic, padding or lookup between the two
      // would show up right here.
      expect(href).toBe(`https://www.wowhead.com/item=${known.blizzardItemId}`);
    }
  });

  it('uses the bare item= path form, with no locale prefix and no query string', () => {
    // Pinned deliberately: a locale prefix or a ?domain= style query would still work in a browser
    // today, and would quietly couple us to a URL shape Wowhead has never promised to keep.
    fixture.componentRef.setInput('item', item(19019));
    fixture.detectChanges();

    expect(
      (fixture.nativeElement as HTMLElement).querySelector('a.item')?.getAttribute('href'),
    ).toBe('https://www.wowhead.com/item=19019');
  });

  it('renders no anchor at all for an empty slot, rather than a link to item=0', () => {
    // item=0 resolves to a Wowhead error page. Sending somebody there would look like our data is
    // wrong rather than simply absent.
    fixture.componentRef.setInput('item', null);
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('a.item')).toBeNull();
  });

  function item(blizzardItemId: number): EquippedItemServiceModel {
    return {
      slot: 'MainHand',
      blizzardItemId,
      itemName: 'Pinned',
      quality: 'Legendary',
      itemLevel: 80,
      iconUrl: null,
    };
  }
});
