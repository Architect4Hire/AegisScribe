---
paths:
  - src/web/**
  - design/**
---
# Frontend rules — Angular + TypeScript (Aspire)

**The visual source of truth is `design/aegisscribe-armory.html`** — a standalone page (no build step, open
it in a browser) defining the AegisScribe token set, every shared component, and all six screens with their
loading, empty, degraded and error states. Read it before styling anything, and follow
`.claude/skills/aegisscribe-design-system/SKILL.md` for the procedure. Tokens are **copied** from that file,
never invented: a literal hex anywhere under `src/web/src/app/` is a defect, and the `design-review`
subagent greps for exactly that.

The one idea the visual system rests on: **three colour systems that never mix.** Chrome (gunmetal +
one brass accent) for the interface; the game's eight item-quality values for item names and item
borders; muted semantic colours for status pills and tints. The interface is near-monochrome *because*
the quality palette already owns green, blue, purple and orange — a new UI hue would compete with the
only colour on the screen that carries data.

- **Standalone components** (no NgModules). One feature per folder.
- **Strict TypeScript.** No `any`. Model interfaces mirror the backend **ServiceModels** exactly, and
  they all live in `src/web/src/app/models/` — one file per feature area
  (`character.models.ts`, `item.models.ts`, `guild.models.ts`, `auth.models.ts`, `ai.models.ts`).
  One known location is what makes the `api-contract-checker` subagent able to find drift; scattering
  interfaces next to the components that use them defeats it. Add a `// Mirrors \`X\`` comment naming
  the C# record, so the pairing is explicit rather than inferred.
- **The SPA is its own deployable, on its own hostname.** `AegisScribe.Web` (a thin ASP.NET Core host)
  serves the built bundle at `app.aegisscribe.com`; every API call goes to the gateway at
  **`bff.aegisscribe.com`**. Cross-origin, same-site. The SPA never calls `api.*` — that is a Blocker.
- **The gateway base URL comes from runtime config**, fetched from the web host at startup — not from
  Angular's `environment.ts` and not baked in at build time. The same artifact has to promote from dev
  to production without a rebuild. Never hardcode `localhost:port`.
- **`withCredentials: true` is mandatory and load-bearing**, set centrally in the interceptor. The
  request is cross-origin, so without it the browser sends no cookie and every call is anonymous.
- **The SPA never handles a token.** No JWT in `localStorage`, no `Authorization` header built in
  TypeScript, no refresh timer. The gateway owns all of it — see `.claude/rules/gateway.md`.
- **Data access through services.** Components never call `HttpClient` directly; use a typed service.
  One service per resource (`CharacterService`, `ItemService`, `GuildService`, `ArmoryAiService`).
- **Auth**: functional interceptor with `withCredentials: true`, functional `CanActivateFn` guards,
  401 handled once in the interceptor. See `.claude/rules/auth.md` — the Angular half is there.
- **Subscriptions.** Prefer the `async` pipe. If you must subscribe, clean up with
  `takeUntilDestroyed` / `DestroyRef`.
- **Scaffolding.** Generate with `ng generate component <feature>/<name>` (or the `new-component`
  skill) so structure stays consistent.
- **Naming.** kebab-case filenames, PascalCase classes, camelCase members.

## Tenant context is part of the chrome

The app is multi-tenant, and the UI has to make the active community obvious at all times — a user who
is an officer in one guild and a member in another will otherwise take an action in the wrong place.

- **The tenant slug lives in the route**, mirroring the API: `/t/:tenantSlug/roster`. Services build
  URLs from the active route's tenant, never from a stored variable that can drift out of sync with
  what the user is looking at.
- A **tenant switcher** sits in the app header, always visible, showing the active community and the
  user's role in it. Switching navigates; it does not mutate hidden state.
- A **tenant guard** (`CanActivateFn`) confirms membership before the route activates.
- A **404 on a tenant route means "not yours"** — route to the tenant picker, don't render an error.
  Never distinguish "doesn't exist" from "not a member" in the UI, because the API deliberately
  doesn't either.
- Role-conditional UI **hides**; it never **enforces**. Every protected view has a server policy behind
  it.

## AegisScribe UI

The component map is in the design reference (§09) and is the build order: shared primitives first
(`scribe-item-cell`, `scribe-stat-tile`, `scribe-status-pill`, `scribe-filter-chip`, `scribe-meter`,
`scribe-empty-state`, `scribe-skeleton`, `scribe-ai-badge`, `scribe-tenant-switcher`,
`scribe-rank-pill`, `scribe-signup-chip`), then the features:

- **Armory** — `character-profile` with `character-banner` / `equipment-rail` / `progression-panel` /
  `profession-panel`; `character-search`; `item-search`
- **Roster** — `roster-table` (tenant ranks, alt grouping, sortable), `roster-entry-detail`,
  `nl-query-bar`
- **Guild** — `guild-overview`, `rank-manager` (the community's own ladder), `member-management`
- **Calendar** — `calendar-month`, `event-detail` with `signup-panel`, `attendance-panel`,
  `event-composer` (including the NL scheduling preview)
- **Notifications** — `notification-centre` (bell, unread state), `notification-preferences`,
  `discord-webhook-settings`
- **AI** — `crafting-advisor`, `composition-analysis`, `attendance-insight`
- **Account** — `login` / `register`, `tenant-picker`, `me`, and a `platform-admin` area behind
  `PlatformAdmin`

**Times are the calendar's hard problem.** Guilds are international. Store UTC, render in the
**tenant's** configured timezone by default with the user's local time available on hover, and always
label which one is shown. A raid time that's ambiguous by an hour is worse than no calendar.

Build the primitives before the features. An item cell re-implemented per feature is how a design
system dies in week three, and this one carries the Wowhead contract.

**Class colour** is set once as an inline `--class-color` custom property on the outermost element
representing a character — banner, roster row, search result — and inherited by descendants. Never an
`[ngClass]` on the class name, never a class-name-to-hex map in TypeScript. The value comes from the
ServiceModel.

**Every data-bound component ships four states**: loading (a skeleton shaped like the content), empty,
**degraded**, and error. Degraded is the one that gets skipped and the one that matters most here — the
data layer returns a stale row rather than throwing when Blizzard is unreachable, so the UI's job is to
show that data and say how old it is. Never a blank error page over data we still hold.

## Wowhead tooltips — the only Wowhead integration there is

Load the script once, at the app shell, and configure it before it loads:

```html
<script>const whTooltips = { colorLinks: true, iconizeLinks: true, renameLinks: false };</script>
<script src="https://wow.zamimg.com/js/tooltips.js"></script>
```

Then every item is a plain outbound anchor built from the Blizzard item id:

```html
<a [href]="'https://www.wowhead.com/item=' + item.blizzardItemId" rel="noopener noreferrer" target="_blank">
  {{ item.name }}
</a>
```

Notes that matter:

- Older guides reference `power.js`. That name is legacy; `tooltips.js` is current.
- `renameLinks: false` — the script would otherwise overwrite our own rendered names with Wowhead's,
  which quietly breaks any component that measures or matches on that text.
- The tooltip is a third-party script talking to Wowhead's servers on hover. **The server never
  calls Wowhead** (see `.claude/rules/external.md`), and neither does our own TypeScript — we render
  a link and the script does the rest.
- Wrap it in one small `WowheadLinkComponent` rather than repeating the URL shape across templates.
  One place to change if the assumption that Wowhead ids match Blizzard ids ever stops holding.

## AI features in the UI

- The crafting advisor is a streaming chat. Render tokens as they arrive, and always show a visible
  stop control — a model call is slower than any other request in the app and users need an exit.
- **Label AI-generated content as generated.** The character and guild summaries are model prose, not
  facts from Blizzard; the UI says so.
- Never send another user's data up in a chat request. The client sends the question and the
  character it's about; the server assembles the grounding context.

## Images come from Blizzard, referenced directly

Every icon, portrait and crest is a `render.worldofwarcraft.com` URL that the API stored on the entity
when it synced. The client references those URLs directly — we do **not** proxy or re-host them, and
we never resolve icon filenames against `wow.zamimg.com` (Wowhead's CDN), which would put a third
party on the critical path of every page and is the server-side dependency the external rules exist to
prevent. The endpoint catalogue is in
`.claude/skills/add-external-sync/references/blizzard-endpoints.md` → "Images and media".

Two consequences for components:

- **Every image needs a real fallback.** A character can have no renders, and item and spell icons
  genuinely 404 for some ids. The silhouette and the neutral icon tile are designed states, not error
  handling.
- **Icons are small — 36 or 56 px, and that's all there is.** Blizzard exposes no 3D render or large
  art for an item. The item cell's 34px tile is sized to that reality; a design asking for a big item
  image has no data source and needs rethinking, not a workaround.
- **Don't assume icon fidelity.** A documented set of spell icons has been plainly wrong for years.
  The Wowhead link the item cell already renders is the user's escape hatch, which is one more reason
  it earns its place.
- **A guild crest is composed, not fetched.** Emblem, border and three colours, layered. The tenant
  switcher's crest should be the community's linked guild crest where one exists, falling back to the
  initial tile in the design reference.

## Attribution

The footer names Blizzard as the source of the game data, without wording that implies endorsement
or affiliation. This is a Terms-of-Use obligation, not a courtesy — see CLAUDE.md → Restrictions.
