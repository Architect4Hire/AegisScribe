# Blizzard Terms of Use & rate limits — the parts that are architectural

> Reference for the `add-external-sync` skill. Quotations are from Blizzard's Developer API Terms of
> Use (https://www.blizzard.com/en-us/legal/a2989b50-5f16-43b1-abec-2ae17cc09dd6/blizzard-developer-api-terms-of-use).
> **Re-read the live terms before shipping** — they change, and this file is a summary.

These are not style preferences. Each one below shows up as a design constraint somewhere in the
codebase, and CLAUDE.md → Restrictions treats them as binding.

## Rate limits

- **36,000 calls per hour.** Stated in the ToU: *"You are limited to thirty-six thousand (36,000)
  calls to the Blizzard Developer API per hour or such other limitation as Blizzard may deem
  appropriate."*
- **A per-second cap also exists**, shown on the developer portal dashboard for our specific client
  rather than in the public terms. The figure developers consistently report is 100/second — treat
  that as a working assumption and confirm it on the dashboard once credentials exist.
- Design consequence: **one shared rate limiter in front of the gateway**, and bounded concurrency
  everywhere that iterates. `Task.WhenAll` over a guild roster is the canonical way to burn an hour's
  budget in ninety seconds.
- On **429**, back off using the response's retry hint. Don't retry immediately, and don't retry
  forever.

## Caching and the 30-day rule

The ToU **permits** storing API data in our own database — on a condition:

> *"You must refresh your data no less frequently than every thirty (30) days."*

This is why the sync worker exists at all. The lazy, request-driven refresh in the DataLayer only
touches what someone looked at; the worker's job is everything else.

Design consequences:
- **No configured TTL may exceed 30 days.** There is a unit test asserting this, because it's the
  setting most likely to get quietly raised by someone tuning performance.
- Every Blizzard-derived entity carries **`LastSyncedAt`**, and it is the column the worker queries.
- Prefer refreshing well inside the window (7–14 days for characters, longer for static reference
  data) so a worker outage doesn't immediately put us out of compliance.

## Deletion requests

> *"If any individual requests that you cease using their Data ... you must immediately cease using
> the Blizzard Developer API and Data from Your Application, and delete all copies of the
> individual's Data in your possession or under your control."*

Design consequences:
- Every Blizzard-derived entity carries a **stable source id**, and the deletion routine executes
  against it.
- A new synced table that the deletion path doesn't know about is a compliance gap, not a TODO.
- The deletion must also stop *re-*syncing that character — a tombstone the sync worker respects, not
  just a `DELETE`.

## Attribution, trademarks, and use

> *"You shall clearly and conspicuously identify Blizzard in Your Application as the source of the
> Data ... in such a way which makes it not appear that Blizzard is endorsing or affiliated with Your
> Application. Additionally, Your Application shall not contain any of Blizzard's trademarks as a
> part of its title or URL."*

Design consequences:
- The UI footer names Blizzard as the data source, without endorsement wording.
- **The project is called AegisScribe** and not something containing *Warcraft*, *WoW*, *Azeroth*,
  *Battle.net*, *Blizzard*, or an expansion name. Same for any domain it's deployed to.

> *"You may not use the Blizzard Developer APIs or Data to market or promote Your or a third party's
> products or services."* and *"You may not sell, license or otherwise transfer the Data to any third
> party."*

No ads against this data, no data export product, no reselling.

## Wowhead — a different regime entirely

Wowhead is a Fanbyte property. Its terms explicitly bar automated access: *"attempt to access or
search the Service or download content from the Service using any engine, software, tool, agent,
device or mechanism (including spiders, robots, crawlers, data mining tools or the like)"*.

There is **no public Wowhead API**. The only sanctioned integration is the client-side tooltip
script decorating outbound links — which is unambiguously fine, and is used by official Blizzard
tools. See `.claude/rules/external.md` and `.claude/rules/frontend.md`.

Everything a crafting feature needs — reagents, quantities, crafted items, skill tiers — is in the
Blizzard Game Data profession endpoints. Reach for those.

## Item ID parity — assumption, not documented fact

Wowhead URLs use bare numeric ids (`wowhead.com/item=19019`) that appear to match Blizzard's item
ids. Every tool in the ecosystem relies on this, but Wowhead does not document it as a guarantee.

The repo pins the assumption with a test over a handful of well-known ids rather than trusting it. If
that test ever fails, the linking strategy is what's wrong — not the test.
