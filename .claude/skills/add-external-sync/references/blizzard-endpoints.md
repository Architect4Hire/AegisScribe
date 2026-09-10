# Blizzard API — hosts, auth, namespaces, endpoints

> Reference for the `add-external-sync` skill. Compiled from Blizzard's developer documentation and
> corroborated across several maintained client libraries. **Verify against
> https://develop.battle.net before relying on an unfamiliar path** — this file is a map, not the
> territory, and Blizzard adds endpoints every expansion.

## Hosts

| Region | API host | OAuth host |
|---|---|---|
| US | `https://us.api.blizzard.com` | `https://us.battle.net` |
| EU | `https://eu.api.blizzard.com` | `https://eu.battle.net` |
| KR | `https://kr.api.blizzard.com` | `https://kr.battle.net` |
| TW | `https://tw.api.blizzard.com` | `https://tw.battle.net` |
| SEA | `https://sea.api.blizzard.com` | `https://sea.battle.net` |
| CN | `https://gateway.battlenet.com.cn` | separate gateway entirely |

Use the **regional** OAuth host (`https://{region}.battle.net/oauth/token`). The non-regional
`oauth.battle.net` has a documented history of intermittent 403s. China is effectively a different
integration, not another region value — out of scope for this build.

## Auth

Client-credentials flow, which is all this app needs:

```
POST https://us.battle.net/oauth/token
Authorization: Basic base64(client_id:client_secret)
Content-Type: application/x-www-form-urlencoded

grant_type=client_credentials
```

Returns `access_token`, `token_type: bearer`, `expires_in` (seconds — typically a long-lived token,
but read the value, don't assume it).

Send it as **`Authorization: Bearer <token>`**. Passing it as `?access_token=` was disallowed in
2024 and will fail.

**What client credentials get you:** all Game Data (`/data/wow/...`) *and* the public character
profile endpoints (`/profile/wow/character/...`). You can render a full armory page for any character
that hasn't been hidden, with no user login.

**What needs a user access token** (authorization-code flow, `wow.profile` scope) — and is therefore
out of bounds for this build: `/profile/user/wow` (the token owner's own character list),
`/profile/user/wow/protected-character/...`, and account-wide collections.

## Namespaces

Every request needs `?namespace=...&locale=...` as **query parameters**. Locale is not a header —
`Accept-Language` does nothing here.

| Namespace | Use for |
|---|---|
| `static-{region}` | Reference data: items, classes, races, specs, professions, recipes, item sets |
| `dynamic-{region}` | Data that moves: realm status, connected realms, PvP seasons, keystone leaderboards |
| `profile-{region}` | Anything about a character, account, **or guild** |

The guild trap: guild endpoints sit under `/data/wow/guild/...` but take **`profile-{region}`**. The
path prefix and the namespace disagree, and a `static-` namespace there returns a 404 that looks like
a missing guild.

Locales: `en_US`, `en_GB`, `de_DE`, `fr_FR`, `es_ES`, `es_MX`, `pt_BR`, `it_IT`, `ru_RU`, `ko_KR`,
`zh_TW`, `zh_CN`.

## Character profile — `namespace=profile-{region}`

Realm is a **slug** (`argent-dawn`), character name is **lowercased**.

```
/profile/wow/character/{realmSlug}/{characterName}                        summary
/profile/wow/character/{realmSlug}/{characterName}/status                 is this a valid, visible character
/profile/wow/character/{realmSlug}/{characterName}/equipment
/profile/wow/character/{realmSlug}/{characterName}/specializations
/profile/wow/character/{realmSlug}/{characterName}/character-media         renders / avatar
/profile/wow/character/{realmSlug}/{characterName}/appearance
/profile/wow/character/{realmSlug}/{characterName}/professions
/profile/wow/character/{realmSlug}/{characterName}/achievements
/profile/wow/character/{realmSlug}/{characterName}/statistics
/profile/wow/character/{realmSlug}/{characterName}/titles
/profile/wow/character/{realmSlug}/{characterName}/reputations
/profile/wow/character/{realmSlug}/{characterName}/pvp-summary
/profile/wow/character/{realmSlug}/{characterName}/encounters/raids
/profile/wow/character/{realmSlug}/{characterName}/encounters/dungeons
/profile/wow/character/{realmSlug}/{characterName}/mythic-keystone-profile
/profile/wow/character/{realmSlug}/{characterName}/collections/mounts
/profile/wow/character/{realmSlug}/{characterName}/collections/pets
/profile/wow/character/{realmSlug}/{characterName}/collections/toys
/profile/wow/character/{realmSlug}/{characterName}/collections/heirlooms
```

`/status` is the cheap existence check — use it before a fan-out over the other twelve.

## Guild — `namespace=profile-{region}`, despite the `/data/wow/` path

```
/data/wow/guild/{realmSlug}/{guildNameSlug}
/data/wow/guild/{realmSlug}/{guildNameSlug}/roster
/data/wow/guild/{realmSlug}/{guildNameSlug}/achievements
/data/wow/guild/{realmSlug}/{guildNameSlug}/activity
```

## Items — `namespace=static-{region}`

```
/data/wow/item/{itemId}
/data/wow/media/item/{itemId}
/data/wow/item-class/index
/data/wow/item-class/{itemClassId}
/data/wow/item-class/{itemClassId}/item-subclass/{itemSubclassId}
/data/wow/item-set/index
/data/wow/item-set/{itemSetId}
/data/wow/search/item                       search endpoint — paged, field-filtered
```

## Professions and recipes — `namespace=static-{region}`

This is the crafting data. All of it. There is no reason to look at Wowhead for any of this.

```
/data/wow/profession/index
/data/wow/profession/{professionId}
/data/wow/profession/{professionId}/skill-tier/{skillTierId}     ← the recipe list for a tier
/data/wow/media/profession/{professionId}
/data/wow/recipe/{recipeId}                                      ← reagents, crafted item, quantities
/data/wow/media/recipe/{recipeId}
```

## Realms — `namespace=dynamic-{region}`

```
/data/wow/realm/index
/data/wow/realm/{realmSlug}
/data/wow/search/realm
/data/wow/connected-realm/index
/data/wow/connected-realm/{connectedRealmId}
```

## Playable class / race / spec — `namespace=static-{region}`

```
/data/wow/playable-class/index
/data/wow/playable-class/{classId}
/data/wow/media/playable-class/{classId}
/data/wow/playable-race/index
/data/wow/playable-race/{raceId}
/data/wow/playable-specialization/index
/data/wow/playable-specialization/{specId}
/data/wow/media/playable-specialization/{specId}
```

## Images and media — Blizzard is the only image source

**Every image in this app comes from here.** WarcraftLogs is a combat-log API and hosts nothing you
would render; Wowhead is out of bounds server-side. So when a screen needs an icon, a portrait or a
crest, it comes from a Blizzard media endpoint.

A media response is a small JSON document containing an `assets` array of
`{ key, value, file_data_id }`, where `value` is an absolute URL on `render.worldofwarcraft.com`.
You store the **URL**, not the bytes.

| What | Endpoint | Namespace |
|---|---|---|
| **Item icon** | `/data/wow/media/item/{itemId}` | `static-{region}` |
| **Spell icon** | `/data/wow/media/spell/{spellId}` | `static-{region}` |
| Playable class icon | `/data/wow/media/playable-class/{classId}` | `static-{region}` |
| Specialization icon | `/data/wow/media/playable-specialization/{specId}` | `static-{region}` |
| Profession icon | `/data/wow/media/profession/{professionId}` | `static-{region}` |
| Recipe icon | `/data/wow/media/recipe/{recipeId}` | `static-{region}` |
| Creature display | `/data/wow/media/creature-display/{creatureDisplayId}` | `static-{region}` |
| Creature family | `/data/wow/media/creature-family/{creatureFamilyId}` | `static-{region}` |
| **Guild crest index** | `/data/wow/guild-crest/index` | `static-{region}` |
| **Guild crest border** | `/data/wow/media/guild-crest/border/{borderId}` | `static-{region}` |
| **Guild crest emblem** | `/data/wow/media/guild-crest/emblem/{emblemId}` | `static-{region}` |
| Character renders | `/profile/wow/character/{realm}/{name}/character-media` | `profile-{region}` |
| Media search | `/data/wow/search/media` | `static-{region}` |

Four things that save time here:

- **The guild-crest index and its media sit at different path shapes.** The index is
  `/data/wow/guild-crest/index`; the images are under `/data/wow/media/guild-crest/...`. Easy to get
  wrong, and the wrong one 404s.
- **A guild crest is composed, not fetched whole.** The guild summary gives you an emblem id, a border
  id and three colour values (emblem, border, background); you render them as layers. That is what the
  tenant crest in the app chrome should be, rather than an initial in a box.
- **Character media has several keys** — typically `avatar`, `inset` and `main-raw`. Pick by use: the
  avatar for roster rows and the switcher, `main-raw` for the profile render. A character can be
  missing renders entirely, so the silhouette fallback is a real state, not a nicety.
- **Media URLs are a separate cache concern.** They change rarely but they do change (a transmog
  update alters a character render). Store them on the entity beside `LastSyncedAt` and refresh them
  with the entity, not on their own schedule.

Serve them by referencing the Blizzard URL directly from the client. **Do not proxy or re-host the
images through the API** — it buys nothing, adds bandwidth and a cache you'd have to invalidate, and
the ToU's attribution and no-resale terms are easier to honour when the asset is plainly Blizzard's.

### Item and spell icons — what you actually get

These two are the ones the UI leans on hardest, so know their shape and their limits before designing
around them.

**You get an icon, and only an icon.** The URL pattern is
`https://render-us.worldofwarcraft.com/icons/{size}/{icon_name}.jpg`, with **36** and **56** the sizes
observed in the wild. There is **no 3D render, no model image and no large art** for an item — if a
design calls for a big item image, Blizzard has nothing to give it, and that has to change the design
rather than the data source. Our item cell renders at 34px, which is exactly why it fits.

**Icon names are shared across many items.** Tens of thousands of items resolve to a far smaller set
of distinct icon filenames. Dedupe media fetches by icon name and the catalogue sync's media cost
collapses — this is the single biggest rate-limit saving available in Phase 12.

**Two documented failure modes, both years old and neither likely to be fixed:**

- **Some icons 404.** Spell ids 471195 (Lay on Hands) and 110960 (Greater Invisibility) are reported
  examples, and there are many others. A 404 on a media fetch is a **normal answer**, exactly like a
  404 on a character — return null, record that there's no icon, and move on. It must never fail a
  sync or a request.
- **Some icons are simply wrong.** Spell 264735 (Survival of the Fittest) returns a lightning-shield
  icon; Corruption returns `spell_shadow_abominationexplosion.jpg`, which is nothing like the in-game
  art. Several have been wrong since Legion's beta.

Design consequences: every icon needs a **fallback tile**, and nothing may assume icon fidelity. The
Wowhead link the item cell already renders is the user's escape hatch when the art looks wrong —
another reason that link earns its place.

## Response shapes — two things to expect

Blizzard responses are **HAL-ish**: nested objects carry a `key.href` you can follow, an `id`, and a
localized `name`. Model the `id` and `name`; don't chase `href`s at request time — that's how one
character page becomes forty API calls.

Fields are **more optional than they look**. A character can have no guild, no active spec, no
equipped items in a slot, and a name with characters that need care in a URL. Build the fixtures for
your gateway tests from captured real responses, not from hand-written JSON that only contains the
happy case.
