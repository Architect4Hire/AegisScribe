# AegisScribe tokens — transcription target

> Reference for the `aegisscribe-design-system` skill. This is the same token set as the `:root` block in
> `design/aegisscribe-armory.html`, in a form that can be copied straight into
> `src/web/src/styles/_tokens.scss`. **The HTML file is authoritative** — if these ever disagree, the
> HTML wins and this file is the one to fix.

```scss
:root {
  /* ── ground & surfaces ─────────────────────────────────────────────────
     Cold gunmetal, hue-biased toward the brass accent's complement so the
     neutrals read as chosen rather than inherited grey. */
  --ground:        #0A0E13;
  --surface-1:     #111823;
  --surface-2:     #18212D;
  --surface-3:     #212C3A;
  --line:          #26313F;
  --line-soft:     #1C2531;
  --line-strong:   #38465A;

  /* ── ink ──────────────────────────────────────────────────────────────
     --ink-3 is for labels. Never for content a user has to read. */
  --ink:           #E6EBF2;
  --ink-2:         #97A3B4;
  --ink-3:         #616D7E;

  /* ── accent ───────────────────────────────────────────────────────────
     Three roles only: active nav item, focus ring, primary action.
     (Plus the AI marker — which works because brass is otherwise rare.) */
  --brass:         #CBA76A;
  --brass-bright:  #E3C48D;
  --brass-dim:     rgba(203, 167, 106, 0.14);
  --brass-line:    rgba(203, 167, 106, 0.38);

  /* ── semantic state ───────────────────────────────────────────────────
     Pills and tints only. Never on an item name or an item border. */
  --ok:            #3E9C77;
  --ok-bg:         rgba(62, 156, 119, 0.14);
  --attention:     #D08A2C;
  --attention-bg:  rgba(208, 138, 44, 0.14);
  --critical:      #C25A50;
  --critical-bg:   rgba(194, 90, 80, 0.14);

  /* ── item quality ─────────────────────────────────────────────────────
     The game's values. Borders, dots and swatches use these. */
  --q-poor:        #9D9D9D;
  --q-common:      #FFFFFF;
  --q-uncommon:    #1EFF00;
  --q-rare:        #0070DD;
  --q-epic:        #A335EE;
  --q-legendary:   #FF8000;
  --q-artifact:    #E6CC80;
  --q-heirloom:    #00CCFF;

  /* Text-on-dark variants. Rare and epic fail contrast as 13px text on our
     ground, so item NAMES use these two. Every other quality uses one value
     for both roles. */
  --q-rare-text:   #3D9BFF;
  --q-epic-text:   #C77DFF;

  /* ── class colour ─────────────────────────────────────────────────────
     Never referenced directly by a component. The API returns the value and
     it is set as --class-color on the character's root element. These exist
     so the mapping has one home. */
  --c-death-knight: #C41E3A;
  --c-demon-hunter: #A330C9;
  --c-druid:        #FF7C0A;
  --c-evoker:       #33937F;
  --c-hunter:       #AAD372;
  --c-mage:         #3FC7EB;
  --c-monk:         #00FF98;
  --c-paladin:      #F48CBA;
  --c-priest:       #FFFFFF;
  --c-rogue:        #FFF468;
  --c-shaman:       #0070DD;
  --c-warlock:      #8788EE;
  --c-warrior:      #C69B6D;

  /* ── type ─────────────────────────────────────────────────────────────
     Condensed grotesque for the interface: armory screens are label-heavy and
     condensed type buys a column of width without shrinking the type.
     Monospace with tabular figures wherever digits sit in a column. */
  --font-display:  "Cinzel", "Iowan Old Style", Georgia, serif;          /* wordmark only */
  --font-ui:       "Barlow Semi Condensed", "Helvetica Neue", Arial, sans-serif;
  --font-body:     "Barlow", "Helvetica Neue", Arial, sans-serif;
  --font-mono:     "JetBrains Mono", ui-monospace, "SF Mono", Menlo, monospace;

  --t-h1:   1.75rem;      /* 28px — page heading            */
  --t-h2:   1.1875rem;    /* 19px — section heading         */
  --t-h3:   1rem;         /* 16px                            */
  --t-body: 0.9375rem;    /* 15px — prose, max 66ch          */
  --t-sm:   0.8125rem;    /* 13px — dense UI, table cells    */
  --t-xs:   0.6875rem;    /* 11px — uppercase labels, .10em  */

  /* ── space (4px base) ─────────────────────────────────────────────────*/
  --s-1: 4px;  --s-2: 8px;  --s-3: 12px; --s-4: 16px;
  --s-5: 24px; --s-6: 32px; --s-7: 48px; --s-8: 64px;

  /* ── form ─────────────────────────────────────────────────────────────
     Radii stay small on purpose: a 12px corner on a stat tile reads as a
     consumer app, and this is a tool people keep open on a second monitor. */
  --r-1: 3px;      /* controls, item cells   */
  --r-2: 6px;      /* panels, frames         */
  --r-pill: 999px; /* status pills only      */

  --shadow-raise: 0 1px 0 rgba(255,255,255,.03), 0 8px 24px rgba(0,0,0,.45);
}
```

## Fonts

Four families, four roles. Load them in `index.html`, with the fallback stacks above declared so a
failed font request degrades rather than reflows into something unreadable:

```html
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Barlow:wght@400;500;600&family=Barlow+Semi+Condensed:wght@400;500;600;700&family=Cinzel:wght@600&family=JetBrains+Mono:wght@400;500;700&display=swap">
```

Weights actually used: Barlow 400/500/600 · Barlow Semi Condensed 400/500/600/700 · Cinzel 600 ·
JetBrains Mono 400/500/700. Don't request the rest.

## Two rules that are easy to lose in transcription

**Tabular figures wherever numbers align.** Item levels, M+ scores, counts, IDs, table columns:

```scss
.item-ilvl, .tile-value, td.num { font-variant-numeric: tabular-nums; }
```

Without it the roster's item-level column jitters row to row, which is the single most noticeable
polish failure in a data-dense UI.

**Prose caps at 66 characters.** `--t-body` text — AI answers, empty-state copy, descriptions —
gets `max-width: 66ch`. Dense UI text at `--t-sm` doesn't; it lives in cells that set their own width.
