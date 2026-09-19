// What the realm-and-name forms share: linking a guild and adding a character both ask for a region and
// a realm the way the game shows them.

// Blizzard's four public API regions. The set the forms offer, not a rule — the server validates the
// region, and a new one would be a one-line change.
export const REGIONS = ['us', 'eu', 'kr', 'tw'] as const;

// "Argent Dawn" → "argent-dawn", "Mal'Ganis" → "malganis", "Area 52" → "area-52". The officer types the
// realm as the game shows it; the API wants the slug and validates whatever arrives, so this is a
// convenience, not the authority.
export function toRealmSlug(realm: string): string {
  return realm
    .trim()
    .toLowerCase()
    .replace(/['’]/g, '')
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '');
}
