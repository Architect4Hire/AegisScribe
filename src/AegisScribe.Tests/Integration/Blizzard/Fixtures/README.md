# Blizzard response fixtures

The bodies in this folder stand in for captured responses from
`https://{region}.api.blizzard.com/profile/wow/character/...`. They are **reconstructed** from the
shapes documented in `.claude/skills/add-external-sync/references/blizzard-endpoints.md` and from
Blizzard's developer documentation — they were not captured from a live call, because the machine
that wrote them had no Blizzard credentials configured.

That distinction matters, so it is recorded here rather than assumed away. **Replace them with real
captures when credentials are available**: `add-external-sync` asks for captured bodies precisely
because hand-written JSON tends to contain only the happy case, and the shapes that break a mapper
are the ones nobody thinks to write down.

To capture a replacement:

```bash
TOKEN=$(curl -s -u "$BLIZZARD_CLIENT_ID:$BLIZZARD_CLIENT_SECRET" \
  -d grant_type=client_credentials https://us.battle.net/oauth/token | jq -r .access_token)

curl -s -H "Authorization: Bearer $TOKEN" \
  "https://us.api.blizzard.com/profile/wow/character/argent-dawn/<name>?namespace=profile-us&locale=en_US" \
  | jq . > character-summary.json

curl -s -H "Authorization: Bearer $TOKEN" \
  "https://us.api.blizzard.com/profile/wow/character/argent-dawn/<name>/equipment?namespace=profile-us&locale=en_US" \
  | jq . > character-equipment.json
```

The tests assert on values, so a replacement capture means updating the expectations in
`BlizzardCharacterFetchTests` to match the character you captured. That is the intended cost.

## What each fixture is for

| File | Covers |
|---|---|
| `character-summary.json` | The ordinary case: guilded, specced, max level. HAL `_links` and `key.href` noise included deliberately, so the test proves the mapper ignores it. |
| `character-summary-unspecced.json` | No `guild` and no `active_spec` — both genuinely absent, not null. The reference warns that fields are more optional than they look. |
| `character-equipment.json` | Slot and quality mapping, including a `SHIRT` and a `TABARD` that `EquipmentSlot` has no member for, and a `HEIRLOOM`. |
| `character-equipment-empty.json` | A character with nothing equipped. `equipped_items` is present and empty. |
