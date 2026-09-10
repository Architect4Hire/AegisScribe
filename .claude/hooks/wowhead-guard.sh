#!/usr/bin/env bash
# PreToolUse guard: deny server-side access to Wowhead. exit 2 = deny.
#
# There is no public Wowhead API, and Fanbyte's terms explicitly bar automated access — spiders,
# crawlers, scrapers, "data mining tools or the like". The sanctioned integration is entirely
# client-side: an outbound <a> link plus their tooltip script, both of which live in src/web/.
#
# So this hook is narrow on purpose. It blocks a Wowhead *request* being written into backend code,
# and stays out of the way of the frontend, tests, and documentation. Everything the server could
# want from Wowhead — item stats, reagents, recipe trees — is in the Blizzard Game Data API.
set -euo pipefail
payload="$(cat)"

if command -v jq >/dev/null 2>&1; then
  file="$(printf '%s' "$payload" | jq -r '.tool_input.file_path // empty')"
  cmd="$(printf '%s' "$payload" | jq -r '.tool_input.command // empty')"
else
  file="$(printf '%s' "$payload" \
    | grep -o '"file_path"[[:space:]]*:[[:space:]]*"[^"]*"' \
    | head -1 | sed 's/.*"file_path"[[:space:]]*:[[:space:]]*"//; s/"$//')"
  cmd="$(printf '%s' "$payload" \
    | grep -o '"command"[[:space:]]*:[[:space:]]*"[^"]*"' \
    | head -1 | sed 's/.*"command"[[:space:]]*:[[:space:]]*"//; s/"$//')"
fi

# A shell command has no file_path. Fetching Wowhead from a terminal is the same breach as
# writing the fetch into a file — an ad-hoc seeding curl is exactly how this rule gets broken —
# so a Bash payload skips the path filters below and goes straight to the host+fetcher check.
if [ -z "$file" ] && [ -n "$cmd" ]; then
  :
else
  # The frontend is where Wowhead links and the tooltip script belong. Docs and rules discuss them.
  case "$file" in
    */src/web/*|*.md|*/.claude/*) exit 0 ;;
  esac

  # Only server-side source is in scope.
  case "$file" in
    *.cs|*.fs|*.py|*.js|*.mjs|*.cjs|*.sh|*.ps1|*.sql) ;;
    *) exit 0 ;;
  esac
fi

# A Wowhead/zamimg host next to something that fetches, downloads, or drives a browser.
fetchers='(HttpClient|GetAsync|PostAsync|GetStringAsync|GetStreamAsync|WebClient|RestClient|HtmlWeb|HtmlAgilityPack|AngleSharp|Playwright|Puppeteer|Selenium|WebDriver|requests\.|urllib|httpx|axios|fetch\(|curl|wget|Invoke-WebRequest|BULK INSERT|OPENROWSET)'

if printf '%s' "$payload" | grep -Eiq '(wowhead\.com|zamimg\.com)' \
&& printf '%s' "$payload" | grep -Eq "$fetchers"; then
  echo "wowhead-guard: blocked a server-side request to Wowhead in ${file:-this command}." >&2
  echo "  There is no public Wowhead API and their terms prohibit automated access." >&2
  echo "  Item, spell, recipe and reagent data all come from the Blizzard Game Data API:" >&2
  echo "    /data/wow/item/{id}, /data/wow/recipe/{id}, /data/wow/profession/{id}/skill-tier/{id}" >&2
  echo "  Wowhead is a link target only, rendered client-side in src/web/." >&2
  echo "  See .claude/rules/external.md." >&2
  exit 2
fi

exit 0
