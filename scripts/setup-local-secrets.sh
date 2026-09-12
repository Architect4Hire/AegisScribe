#!/usr/bin/env bash
# Hydrates the four LOCAL Aspire parameters that AegisScribe.AppHost/AppHost.cs declares with
# secret: true and no default (sql-password, redis-password, bff-client-secret,
# ops-client-secret). Without a value, `aspire run` prompts for each of them interactively.
#
# These are not credentials anyone hands you — they're self-issued, local to this machine, and
# only need to be internally consistent with themselves (the same migration-service process that
# seeds the bff/ops OpenIddict clients reads the same parameter the gateway/sync worker present).
# Run this once per machine/clone; re-running it is a no-op for anything already set, because
# rotating sql-password after the SQL container has already initialised its data volume with the
# old one would lock you out of it.
#
# This is separate from the EXTERNAL credentials documented in README.md's "Getting started" step
# 3 (Blizzard, WarcraftLogs) — those are real developer-portal values, genuinely optional, and this
# script never touches them.
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."
PROJECT="src/AegisScribe.AppHost"

already_set() {
  dotnet user-secrets list --project "$PROJECT" 2>/dev/null | grep -q "^Parameters:$1 "
}

hydrate() {
  local key="$1" value="$2"
  if already_set "$key"; then
    echo "Parameters:$key already set — skipping."
  else
    dotnet user-secrets set "Parameters:$key" "$value" --project "$PROJECT" >/dev/null
    echo "Parameters:$key generated."
  fi
}

# SQL Server's complexity rule: 8+ chars, at least 3 of {upper, lower, digit, symbol}. Built
# explicitly rather than trusting a random generator to satisfy it by chance.
sql_password="Aq$(openssl rand -hex 12)!1"

hydrate "sql-password" "$sql_password"
hydrate "redis-password" "$(openssl rand -hex 24)"
hydrate "bff-client-secret" "$(openssl rand -hex 24)"
hydrate "ops-client-secret" "$(openssl rand -hex 24)"

echo "Done. \`aspire run\` should no longer prompt for these four."
