# docs/

Narrative and prompts — how this repo is shaped and how it gets built, not how it behaves.

| File | What it is |
|---|---|
| [`architecture.md`](architecture.md) | High-level architecture: context and container views, the tenancy model, the request path, external integrations, the AI vertical, and the key decisions with their trade-offs. Start here if you're new. |
| [`scrub-prompts.md`](scrub-prompts.md) | The build sequence. Part 1 is a one-time run of 108 seam-by-seam microprompts; Part 2 is reusable operational templates. |

**This folder is not a rule source.** The authority for how the app is built is `CLAUDE.md` at the
repo root, the path-scoped rules in `.claude/rules/`, and the skills in `.claude/skills/`. If anything
here contradicts one of those, the rule wins and the document is what needs fixing — subagents
auditing the codebase are told to ignore `docs/` for exactly this reason.
