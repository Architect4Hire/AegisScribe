#!/usr/bin/env bash
# PreToolUse guard: deny writes containing secret-shaped strings. exit 2 = deny.
#
# Beyond the usual credential shapes, this build has two families worth naming explicitly:
# SQL Server connection strings (which carry the password inline, unlike the URI-style ones) and
# the Blizzard client secret (a 32-char token otherwise indistinguishable from noise — so we match
# on the assignment, not on the value). `?access_token=` is in here too: Blizzard disallowed it in
# 2024, and it leaks the token into every log and referer along the way.
set -uo pipefail

payload="$(cat)"

# Two things about matching against a hook payload, both learned the hard way:
#
#  1. The payload is JSON, so quotes inside file content arrive ESCAPED — a write of
#     `"client_secret": "abc"` reaches us as `\"client_secret\": \"abc\"`. Any pattern that expects
#     a bare quote next to the key silently never fires. Hence Q below: run of quotes/backslashes.
#  2. grep treats a pattern beginning with `-` as an option, so every pattern is passed with `-e`.
Q='["'"'"'\\]*'                     # zero or more quote / apostrophe / backslash
V='[^"'"'"'\\[:space:],;]{8,}'      # a value: 8+ chars that aren't quote, backslash or a delimiter

patterns=(
  'sk-[A-Za-z0-9_-]{16,}'
  'AKIA[0-9A-Z]{16}'
  '-----BEGIN [A-Z ]*PRIVATE KEY-----'
  '(postgres|redis|mongodb|mysql)://[^:@/]+:[^@/]+@'
  "(Password|Pwd)[[:space:]]*=[[:space:]]*[^;\"'[:space:]]{6,};"
  "password${Q}[[:space:]]*[=:][[:space:]]*${Q}${V}"
  "(client[_-]?secret)${Q}[[:space:]]*[=:][[:space:]]*${Q}${V}"
  "(api[_-]?key|subscription[_-]?key|access[_-]?key)${Q}[[:space:]]*[=:][[:space:]]*${Q}${V}"
  '[?&]access_token='
)

for pattern in "${patterns[@]}"; do
  if printf '%s' "$payload" | grep -Eiq -e "$pattern"; then
    echo "secret-guard: blocked a write with a secret-shaped string." >&2
    echo "  Credentials are Aspire parameters (AddParameter(..., secret: true)) set via user secrets." >&2
    echo "  Blizzard tokens go in the Authorization header, never as ?access_token=." >&2
    exit 2
  fi
done

exit 0
