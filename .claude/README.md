# The `.claude/` folder

This folder is the reusable Claude Code toolkit for **AegisScribe** — a multi-tenant World of Warcraft
armory and guild-operations platform built with **Aspire + ASP.NET Core + Angular + .NET MAUI + SQL
Server**, with ASP.NET Core Identity behind OpenIddict, tenant membership, Blizzard and WarcraftLogs
integrations, Discord and push notifications, and an AI vertical on Azure AI Foundry + Semantic
Kernel. The `.claude/` toolkit is as
much the point of the repo as the app itself.

**Start with `rules/tenancy.md`.** Multitenancy is the invariant everything else sits on, and its
failure mode is silent.

> **Important:** the project's main memory file, `CLAUDE.md`, lives at the **repo root**, one level
> *above* this folder — not inside it. Claude Code auto-discovers `CLAUDE.md` by walking up from your
> working directory, and the root copy survives `/compact`.

## What each piece is, and when it loads

| Path | What it is | When it enters context |
|---|---|---|
| `settings.json` | Shared project settings (incl. hook wiring). Committed. | Read at session start. |
| `rules/tenancy.md` | **Read this first.** The global vs tenant-scoped zones, tenant resolution, query filters, cache keying, per-tenant budgets. | Loads across the API, worker, tests and web app. |
| `rules/aspire.md` | AppHost/orchestration conventions, incl. the 2025 SQL image pin. | Loads when Claude touches AppHost/ServiceDefaults. |
| `rules/backend.md` | ASP.NET Core + EF Core conventions. Path-scoped to the API, worker and migrator. | Loads when Claude touches the backend. |
| `rules/gateway.md` | The YARP BFF: three hostnames, the gateway as an OAuth confidential client, header stripping, CORS and cookie attributes across subdomains. | Loads when Claude touches the gateway or web host. |
| `rules/auth.md` | Identity for *who*, tenant membership for *what*. OpenIddict, the three registered clients, refresh rotation, tenant policies, the membership lifecycle. | Loads when Claude touches auth or tenancy code. |
| `rules/api-contract.md` | Versioning and evolution — what breaks a shipped mobile client, cursor pagination, idempotency, `problem+json`, delta sync. | Loads when Claude touches controllers or boundary models. |
| `rules/mobile.md` | The .NET MAUI client: PKCE in the system browser, secure token storage, offline, push. | Loads when Claude touches `src/AegisScribe.Mobile/`. |
| `rules/external.md` | Blizzard and WarcraftLogs gateways, Discord webhooks, per-tenant sync budgets, the data-deletion path, and the Wowhead prohibition. | Loads when Claude touches `Integration/` or the sync worker. |
| `rules/ai.md` | Microsoft.Extensions.AI / SK / Foundry conventions and AI safety invariants. | Loads when Claude touches `Ai/` or `Embeddings/`. |
| `rules/frontend.md` | Angular conventions, the design system, Wowhead tooltips, AI labelling. | Loads when Claude touches `src/web/` or `design/`. |
| `skills/add-endpoint/` | Playbook for adding an API endpoint through the full layer stack. | On demand, when the task matches. |
| `skills/new-component/` | Playbook for adding an Angular component. | On demand, when the task matches. |
| `skills/add-aspire-resource/` | Playbook for adding a locally-orchestrated resource. | On demand, when the task matches. |
| `skills/add-external-sync/` | Playbook for bringing new data across from Blizzard or WarcraftLogs, with endpoint, terms and GraphQL references. | On demand, when the task matches. |
| `skills/add-tenant-entity/` | Playbook for anything a community owns privately — the zone decision, filters, interceptor, tenant cache keys, the two-tenant test. Includes a time-and-recurrence reference for the calendar. | On demand, for tenant-scoped work. |
| `skills/add-notification/` | Playbook for the event → notification → in-app/Discord/push pipeline. | On demand, when the task matches. |
| `skills/add-ai-capability/` | Playbook for SK plugins, semantic search, NL queries and summaries, with vector-search and query-safety references. | On demand, when the task matches. |
| `skills/aegisscribe-design-system/` | How to build Angular UI against `design/aegisscribe-armory.html` — tokens, the three colour systems, component contracts, required states. Includes a `tokens.md` transcription target. | On demand, for any visual work. |
| `skills/aspire*/`, `skills/playwright-cli/` | Six **vendored** skills (`aspire`, `aspire-init`, `aspire-orchestration`, `aspire-deployment`, `aspire-monitoring`, `playwright-cli`). Not ours — kept as shipped so they can be re-vendored. | On demand, when the task matches. |
| `agents/code-reviewer.md` | Read-only reviewer subagent (tenancy/Aspire/Identity/external/AI-aware). | When delegated, or `@code-reviewer`. |
| `agents/test-gap-analyzer.md` | Read-only test-gap subagent, tuned to this repo's four usual gaps. | When delegated, or `@test-gap-analyzer`. |
| `agents/api-contract-checker.md` | Read-only subagent: finds **breaking changes** against the committed OpenAPI document — the ones that strand shipped mobile clients — and drift between boundary types and the Angular models. | When delegated, or `@api-contract-checker`. |
| `agents/skills-evals.md` | Read-only subagent: audits whether generated code actually followed the skills. | When delegated, or `@skills-evals`. |
| `agents/external-compliance.md` | Read-only subagent: audits the Blizzard Terms of Use obligations and the Wowhead prohibition. | When delegated, or `@external-compliance`. |
| `agents/ai-guardrails.md` | Read-only subagent: audits the AI vertical for prompt, cost and injection failure modes. | When delegated, or `@ai-guardrails`. |
| `agents/tenant-isolation-auditor.md` | Read-only subagent: the cross-tenant leak audit. Highest severity in the repo — run it after any tenant-scoped change. | When delegated, or `@tenant-isolation-auditor`. |
| `agents/design-review.md` | Read-only subagent: audits components against the design reference — hard-coded colours, colour systems bleeding, missing states, re-implemented primitives. | When delegated, or `@design-review`. |
| `hooks/format.sh` | Formats the edited file after each edit. | Runs via the `PostToolUse` hook in `settings.json`. |
| `hooks/secret-guard.sh` | Blocks anything credential-shaped, including SQL Server connection strings, the Blizzard client secret, and `?access_token=`. | `PreToolUse` on `Edit\|Write\|MultiEdit\|Bash` — a curl carrying a token is blocked the same as a file containing one. |
| `hooks/wowhead-guard.sh` | Blocks server-side HTTP access to Wowhead. | `PreToolUse` on `Edit\|Write\|MultiEdit\|Bash` — the ad-hoc `curl wowhead.com` is the likeliest breach, so the shell is in scope too. |

Rule of thumb:
- **Rule / CLAUDE.md** = something Claude should *know*.
- **Skill** = a procedure Claude should *follow* when a task matches.
- **Subagent** = work Claude should *delegate* to keep the main context clean.
- **Hook** = something that must happen *no matter what Claude decides*.

The last row is why `wowhead-guard.sh` exists. "Don't scrape Wowhead" is stated in CLAUDE.md, in
`rules/external.md`, and in the compliance agent — but a rule is advice to a model, and this one is a
terms-of-service violation rather than a style preference, so it also gets an enforcement that
doesn't depend on anyone having read anything.

## Not committed (personal / local)
- `settings.local.json` — personal overrides, git-ignored on purpose.
- Anything ending in `.local.*`.

## After cloning
```bash
chmod +x .claude/hooks/*.sh
git update-index --chmod=+x .claude/hooks/*.sh   # record it in git, not just on disk
```
The second line is the one people miss on Windows (`core.fileMode` is usually
`false` there), and a non-executable hook fails **silently** — which for
`secret-guard` and `wowhead-guard` means the guards are simply gone. The repo's
`.gitattributes` pins `*.sh` to LF for the same class of reason: a CRLF shebang
dies with "bad interpreter".
Then open a session: `/memory` confirms the rules load, `/agents` shows the subagents.

You'll also want Blizzard API credentials for anything that syncs. Register a client at
https://develop.battle.net, then set them as user secrets on the AppHost project:
```bash
dotnet user-secrets set "Parameters:blizzard-client-id" "<id>"         --project src/AegisScribe.AppHost
dotnet user-secrets set "Parameters:blizzard-client-secret" "<secret>" --project src/AegisScribe.AppHost
```
WarcraftLogs is optional and separate — register a client at https://www.warcraftlogs.com/api/clients
and set `Parameters:warcraftlogs-client-id` / `-client-secret` the same way.

Without any of them the app still runs — every gateway no-ops and the app serves seeded data in
seeded tenants. That's deliberate; offline development is a first-class case here.

## Verify before trusting

Aspire, Claude Code, SQL Server's vector features and the .NET AI stack all ship fast, and several of
the pieces this repo leans on are **preview**:

- `Aspire.Hosting.Foundry` is preview, and was recently renamed from `Aspire.Hosting.Azure.AIFoundry`.
- SQL Server's approximate vector index (`CREATE VECTOR INDEX`, DiskANN) is preview and needs
  `PREVIEW_FEATURES = ON`. The `VECTOR` type and `VECTOR_DISTANCE` are GA; EF Core 10's mapping of
  them is GA, and `WithApproximate()` is experimental.
- Semantic Kernel remains supported, but Microsoft has positioned the **Microsoft Agent Framework** as
  its successor for agent orchestration. This project deliberately uses SK — that's a decision, not an
  oversight. Don't silently migrate it.
- `Microsoft.SemanticKernel.Connectors.SqlServer` is **deprecated**, renamed to
  `CommunityToolkit.VectorData.SqlServer`. This repo doesn't use either — vectors live on our own EF
  entities — but you'll meet the old name in tutorials.

Confirm the `settings.json` hook syntax, subagent frontmatter, and exact API names against
https://code.claude.com/docs, https://aspire.dev, https://learn.microsoft.com and
https://develop.battle.net.
