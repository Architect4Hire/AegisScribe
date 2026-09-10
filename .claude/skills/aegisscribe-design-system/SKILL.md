---
name: aegisscribe-design-system
description: >
  Build Angular UI that matches the AegisScribe design reference at `design/aegisscribe-armory.html`. Use for any
  visual work in `src/web/` — "style the character page", "build the equipment rail", "make the roster
  table", "add a loading state", "what colour should this be", "add a new component" — and before
  inventing any colour, spacing, radius or type size. Covers the token layer, the item-quality and
  class-colour rules, the shared component contracts, the required states, and the UI obligations
  carried over from CLAUDE.md.
---

# Build against the AegisScribe design system

`design/aegisscribe-armory.html` is the visual source of truth. It is a standalone page — open it in a
browser, no build step. **Read it before styling anything.** It defines the tokens, shows every
shared component, and draws all six screens with their empty, loading, degraded and error states.

The rule is simple and it is the whole point of this skill: **tokens are copied from that file, never
invented.** If a value you need isn't there, the design needs a decision, not a guess — say so rather
than picking a plausible hex.

## Step 0 — the token layer exists once

Before any component work, the reference's `:root` block is transcribed into
`src/web/src/styles/_tokens.scss` and imported globally. That file is a **copy**, not a reinterpretation:
same names, same values, same comments where they explain a decision.

```scss
:root {
  --ground: #0A0E13;  --surface-1: #111823;  --surface-2: #18212D;  --surface-3: #212C3A;
  --line: #26313F;    --line-soft: #1C2531;  --line-strong: #38465A;
  --ink: #E6EBF2;     --ink-2: #97A3B4;      --ink-3: #616D7E;
  --brass: #CBA76A;   --brass-bright: #E3C48D;
  /* … quality, class, semantic, type, space, radius … */
}
```

Component styles reference `var(--…)` only. **A literal hex in a component file is a defect** — the
`design-review` subagent greps for exactly that.

## The three colour systems, and why they never mix

This is the idea the whole system rests on, and it is the thing most likely to be got wrong:

| System | Where it may appear | Never appears on |
|---|---|---|
| **Chrome** (gunmetal + one brass accent) | surfaces, borders, text, the active nav item, the focus ring, the primary button | anything that encodes data |
| **Item quality** (the game's 8 values) | item name colour, item cell left border, quality dots | pills, buttons, backgrounds, status |
| **Semantic state** (ok / attention / critical) | status pills, row stripes, tinted cells | item names, item borders |

The interface is near-monochrome **because** the quality palette already owns green, blue, purple and
orange. Any new saturated UI colour competes with the only colour on the screen that means something.
So when a component wants its own hue, the answer is no — use surface, border and weight instead.

Two consequences worth stating outright:

- **The "weakest slot" flag tints the cell and adds a word. It does not repaint the left border.**
  Borrowing the quality border for a semantic state makes an uncommon belt look legendary.
- **Rare and epic need their text variants.** `#0070DD` and `#A335EE` fail contrast as 13px text on
  our ground. Item *names* use `--q-rare-text` / `--q-epic-text`; borders and dots keep the canonical
  values. Every other quality uses one value for both.

## Class colour is scoped, never hard-coded

Set `--class-color` once, as an inline custom property, on the outermost element representing that
character — the banner, the roster row, the search result card. Descendants inherit it.

```html
<article class="result-card" [style.--class-color]="character.classColor">
  <span class="result-name">{{ character.name }}</span>   <!-- colour: var(--class-color) -->
</article>
```

Never `[ngClass]="'class-' + character.className"`, never a `switch` over class names in a component,
never a class-name-to-hex map in TypeScript. One property, set at the boundary. The class colour comes
from the ServiceModel; the API maps Blizzard's class id to the token value, so the frontend never owns
that table either.

## Shared components come first

Build these before any feature, in `src/web/src/app/shared/`. Every screen depends on them, and an
item cell re-invented per feature is how a design system dies in week three.

| Component | Contract |
|---|---|
| `<scribe-item-cell>` | `item`, `showSlot?`, `flagged?`. Renders the Wowhead anchor. The most-repeated object in the app. |
| `<scribe-stat-tile>` | `label`, `value`, `sub?`. Tabular figures. |
| `<scribe-status-pill>` | `state: 'ok'\|'attention'\|'critical'\|'neutral'`, `label`. Dot **and** word. |
| `<scribe-filter-chip>` | `key`, `value`; emits `removed`. |
| `<scribe-meter>` | `value`, `max`, `variant?`. Raid and profession rows. |
| `<scribe-empty-state>` | `heading`, `body`, `action?`, `variant: 'empty'\|'error'`. |
| `<scribe-skeleton>` | `height`, `count?`. Shape-matched, not a spinner. |
| `<scribe-ai-badge>` | No inputs. Marks model-authored output. Never optional. |

### The item cell in particular

```html
<a class="item" [class]="'q-' + item.quality.toLowerCase()"
   [href]="'https://www.wowhead.com/item=' + item.blizzardItemId"
   target="_blank" rel="noopener noreferrer">
  <img class="item-icon" [src]="item.iconUrl" alt="" />
  <span class="item-body">
    <span class="item-name">{{ item.name }}</span>
    <span class="item-meta">{{ item.slot }} · {{ item.quality }}</span>
  </span>
  <span class="item-flag" *ngIf="flagged">Weakest</span>
  <span class="item-ilvl">{{ item.itemLevel }}</span>
</a>
```

It is an `<a>`, not a `<div>` with a click handler — this is the Wowhead integration, and the tooltip
script attaches to real anchors. `alt=""` on the icon because the name beside it already says what the
item is; a duplicated alt makes screen readers read every item twice.

## States are part of the component, not an afterthought

Every data-bound component ships four states, and the reference draws all of them:

1. **Loading** — a skeleton shaped like the content. A gear rail's skeleton is eight item-cell-shaped
   blocks, not a spinner.
2. **Empty** — says what was searched for and offers the next action. "No character called *X* on
   *Y*", not "No results".
3. **Degraded** — we hold stale data and Blizzard is unreachable. **This is a designed state, not an
   error**: show the data, say how old it is. Never a blank error page over data we still have.
4. **Error** — the request genuinely failed. Say what happened and offer a retry.

Skipping (3) is the characteristic mistake in this app, because the data layer is built to return the
stale row rather than throw — so the UI must have somewhere to say so.

## UI obligations carried from CLAUDE.md

These are drawn in the reference so they can be copied rather than remembered. Each one discharges a
rule; none is a style preference:

- **Blizzard attribution** in the app footer, naming Blizzard as the data source and disclaiming
  affiliation. Terms of Use.
- **No Blizzard trademark** in the wordmark, page titles, or any URL. The product is AegisScribe.
- **Wowhead is client-side only** — item cells render outbound anchors and their script decorates
  them. Configure `whTooltips` with **`renameLinks: false`** or the script overwrites our item names
  and breaks anything matching on that text.
- **AI output is labelled.** `<scribe-ai-badge>` on every model-authored surface; a visible **stop**
  control while a response streams; a grounding disclosure listing sources.
- **Natural-language results show the interpreted filter** as removable chips. The chips *are* the
  constrained filter object, rendered — that's how a user catches a misreading in one glance.
- **Staleness is visible.** A character page states when it last synced.
- **State is never colour alone.** Pills pair a dot with a word; quality pairs a border with a name.
- **Roles hide, servers enforce.** Role-conditional UI is cosmetic; every protected view has a server
  policy behind it.

## Accessibility floor

- Focus is a 2px brass outline at 2px offset. Never removed, never colour-only.
- Body text and values use `--ink`; `--ink-3` is for labels and never for content a user must read.
- Every icon-only control has an `aria-label`. Decorative SVG gets `aria-hidden="true"`.
- Wide content (roster tables, code) scrolls inside its own `overflow-x:auto` container. The page body
  never scrolls sideways.
- Respect `prefers-reduced-motion` — the streaming caret and skeleton shimmer both stop.

## Dark only, by decision

There is no light theme, and that is a choice rather than an omission: an armory is read beside the
game, and the game is dark. Don't add a theme toggle or `prefers-color-scheme` branch unless the
decision is revisited — a half-built light mode is worse than none. Paint backgrounds and colours
explicitly regardless, so no component inherits a ground it didn't set.

## Checklist before done
- [ ] Read `design/aegisscribe-armory.html` for this component before styling it
- [ ] Every colour, space, radius and type size is a `var(--…)` from the token file — **no literal hex**
- [ ] Class colour set once as `--class-color` on the character's root element; no class→hex map in TS
- [ ] Quality drives the item border and name only; rare/epic names use the `-text` variants
- [ ] Semantic state appears on pills and tints only — it never repaints a quality border
- [ ] Shared primitives reused, not re-implemented locally
- [ ] Loading, empty, **degraded** and error states all present
- [ ] Item references render `<scribe-item-cell>` → an outbound Wowhead anchor; nothing fetches Wowhead
- [ ] AI surfaces carry the badge, a stop control while streaming, and a grounding disclosure
- [ ] NL query results show removable interpreted-filter chips
- [ ] Footer carries the Blizzard attribution
- [ ] Focus visible, icon-only controls labelled, wide content scrolls in its own container
- [ ] `ng test` passes; the component renders at 390px with no horizontal page scroll
