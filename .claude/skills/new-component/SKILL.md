---
name: new-component
description: >
  Scaffold a new Angular component in the AegisScribe frontend. Use when creating UI — e.g. "add a
  character-profile component", "make an equipment panel", "build the crafting advisor chat".
  Produces a standalone component wired to a typed service, following this repo's frontend
  conventions and the AegisScribe design system, including Wowhead item links, auth guards, and AI-content
  labelling where relevant.
---

# Add an Angular component

Work in `src/web/`.

> **Styling is not part of this skill.** Anything visual — colour, spacing, type, states, which shared
> primitive to reuse — comes from `.claude/skills/aegisscribe-design-system/SKILL.md` and the reference page
> it governs, `design/aegisscribe-armory.html`. Read that skill alongside this one; this skill is the Angular
> mechanics, that one is the look.

1. **Find it in the design reference first.** Open `design/aegisscribe-armory.html` and locate the component
   or the screen it belongs to. If it's a shared primitive (item cell, stat tile, status pill, filter
   chip, meter, empty state, skeleton, AI badge), it may already exist in `src/web/src/app/shared/` —
   reuse it. If the reference doesn't cover what you're building, the design needs a decision: say so
   rather than inventing tokens.
2. **Generate.** `ng generate component <feature>/<name>` — standalone, kebab-case files.
3. **Types.** Define/reuse a model interface in `src/web/src/app/models/<area>.models.ts` that mirrors
   the backend **ServiceModels** exactly. No `any`. Names are camelCase mirrors of the C# PascalCase
   members, and each interface carries a `// Mirrors \`CharacterDetailServiceModel\`` comment. Models
   go in that folder, never beside the component — the contract checker looks there.
4. **Data access.** Never call the API from the component. Use (or create) a typed service on
   `HttpClient`; read the API base URL from Aspire-injected config, not a hardcoded value. The auth
   interceptor already adds `withCredentials` — don't set it per call.
5. **Auth.** If the route needs a signed-in user, add a functional `CanActivateFn` guard on the
   route. Role-conditional UI hides controls; it never *enforces* — the server policy does that.
6. **Template.** Render async data with the `async` pipe. If you must subscribe, clean up with
   `takeUntilDestroyed` / `DestroyRef`.
7. **Styling.** Every colour, space, radius and type size is a `var(--…)` from
   `src/web/src/styles/_tokens.scss`. **No literal hex in a component file** — that's what
   `@design-review` greps for first. Class colour is an inline `--class-color` on the character's root
   element, never an `[ngClass]` on the class name.
8. **All four states.** Loading (a skeleton shaped like the content, not a spinner), empty,
   **degraded** (we hold stale data and Blizzard is unreachable — show it, say how old it is), and
   error. The degraded state is the one that gets skipped, because the data layer returns the stale row
   rather than throwing, so nothing else forces you to handle it.
9. **Items link to Wowhead.** Any component rendering an item, spell or recipe uses
   `<scribe-item-cell>` rather than hand-writing the URL. It builds
   `https://www.wowhead.com/item=<blizzardItemId>` from the id the API returned, as an `<a>` with
   `target="_blank" rel="noopener noreferrer"`, and the tooltip script in the app shell decorates it.
   The server never calls Wowhead — see `.claude/rules/external.md`.
10. **AI content is labelled.** A component rendering a model-written summary or chat answer carries
    `<scribe-ai-badge>`. Streaming responses get a visible stop control and a grounding disclosure.
    Natural-language query results show the interpreted filter as removable chips.
11. **Tests.** Update `.spec.ts` with a render test and one behavior test. Run `ng test`.

## Notes

Keep presentational and data concerns separate where it helps testability — `scribe-item-cell` takes an
item and renders it; it doesn't fetch one. The component map and build order are in the design
reference (§09): shared primitives before features, always.

## Checklist before done
- [ ] Found in `design/aegisscribe-armory.html` before building; shared primitives reused, not re-implemented
- [ ] Standalone component, kebab-case filenames
- [ ] Model interface in `src/web/src/app/models/`, mirroring the backend ServiceModels
- [ ] API access through a typed service; base URL from injected config
- [ ] `async` pipe used (or subscriptions cleaned up)
- [ ] Protected routes have a functional guard; role-based UI hides but does not enforce
- [ ] Every value is a token — **no literal hex**; class colour via `--class-color`
- [ ] Loading, empty, **degraded** and error states all present
- [ ] Item/spell/recipe references go through `<scribe-item-cell>`; no client-side fetch of Wowhead
- [ ] Model-generated content carries `<scribe-ai-badge>`; streaming views have a stop control
- [ ] Focus visible, icon-only controls labelled, wide content in an `overflow-x:auto` wrapper
- [ ] Renders at 390px with no horizontal page scroll
- [ ] Tests pass (`ng test`)
