using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using Microsoft.EntityFrameworkCore;

namespace AegisScribe.Domain.Data;

public class CharacterRepository(AegisScribeDbContext db) : ICharacterRepository
{
    public Task<Character?> FindByRealmAndNameAsync(string region, string realmSlug, string name, CancellationToken ct)
    {
        var nameLower = name.ToLowerInvariant();
        return db.Characters
            .AsNoTracking()
            .Include(c => c.Realm)
            .Include(c => c.Equipment)
                .ThenInclude(e => e!.EquippedItems)
            .FirstOrDefaultAsync(c =>
                c.Realm.Region == region &&
                c.Realm.Slug == realmSlug &&
                c.NameLower == nameLower, ct);
    }

    public Task<bool> ExistsAsync(Guid characterId, CancellationToken ct) =>
        db.Characters.AsNoTracking().AnyAsync(c => c.Id == characterId, ct);

    // Seek/keyset pagination ordered by (NameLower, Id) — NameLower alone isn't unique (two realms
    // can share a character name), so Id breaks the tie. Standard EF Core keyset predicate: CompareTo
    // translates to SQL Server's native > comparison rather than needing a tuple/row-value compare.
    public async Task<IReadOnlyList<CharacterSummaryServiceModel>> SearchAsync(
        string region, string? realmSlug, string? nameContains, string? afterNameLower, Guid? afterId, int take, CancellationToken ct)
    {
        var query = db.Characters.AsNoTracking().Where(c => c.Realm.Region == region);

        if (!string.IsNullOrWhiteSpace(realmSlug))
        {
            query = query.Where(c => c.Realm.Slug == realmSlug);
        }

        if (!string.IsNullOrWhiteSpace(nameContains))
        {
            // A leading-wildcard LIKE '%needle%' — can't use the (RealmId, NameLower) index, so this
            // scans every character row in scope (region, optionally narrowed by realm). Acceptable at
            // today's data volume; a future version copying this pattern at scale should reconsider
            // (e.g. StartsWith, or a dedicated search index) rather than inheriting this silently.
            var needle = nameContains.ToLowerInvariant();
            query = query.Where(c => c.NameLower.Contains(needle));
        }

        if (afterNameLower is not null && afterId is not null)
        {
            query = query.Where(c =>
                c.NameLower.CompareTo(afterNameLower) > 0 ||
                (c.NameLower == afterNameLower && c.Id.CompareTo(afterId.Value) > 0));
        }

        return await query
            .OrderBy(c => c.NameLower).ThenBy(c => c.Id)
            .Take(take)
            .Select(c => new CharacterSummaryServiceModel
            {
                Id = c.Id,
                RealmSlug = c.Realm.Slug,
                Name = c.Name,
                Level = c.Level,
                Class = c.Class,
                Spec = c.Spec,
                ItemLevel = c.ItemLevel,
                Faction = c.Faction,
                LastSyncedAt = c.LastSyncedAt,
            })
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<StaleCharacterRef>> FindStaleAsync(
        DateTimeOffset staleBefore, int take, CancellationToken ct) =>
        await db.Characters
            .AsNoTracking()
            // Equipment is a LEFT JOIN in SQL, so a character that has never had gear fetched satisfies
            // the null check rather than being silently excluded by the join.
            .Where(c =>
                c.LastSyncedAt < staleBefore
                || c.Equipment == null
                || c.Equipment.LastSyncedAt < staleBefore)
            .OrderBy(c => c.LastSyncedAt)
            .Take(take)
            .Select(c => new StaleCharacterRef(c.Id, c.RealmId, c.Realm.Region, c.Realm.Slug, c.Name))
            .ToListAsync(ct);

    public async Task<Character> UpsertCharacterAsync(Character fresh, Guid realmId, CancellationToken ct)
    {
        var existing = await db.Characters
            .FirstOrDefaultAsync(c => c.RealmId == realmId && c.NameLower == fresh.NameLower, ct);

        // A rename or a realm transfer, and the reason this is not a plain find-by-natural-key upsert.
        // BlizzardCharacterId carries a unique index, so inserting the same character under its new name
        // would violate it — the row has to be found by source id and moved, not duplicated. It is also
        // the only way the stored name ever catches up with a rename.
        existing ??= await db.Characters
            .FirstOrDefaultAsync(c => c.BlizzardCharacterId == fresh.BlizzardCharacterId, ct);

        if (existing is null)
        {
            fresh.Id = Guid.NewGuid();
            fresh.RealmId = realmId;
            db.Characters.Add(fresh);
            return fresh;
        }

        existing.RealmId = realmId;
        existing.Name = fresh.Name;
        existing.NameLower = fresh.NameLower;
        existing.Level = fresh.Level;
        existing.Class = fresh.Class;
        existing.Spec = fresh.Spec;
        existing.ItemLevel = fresh.ItemLevel;
        existing.Faction = fresh.Faction;
        existing.BlizzardCharacterId = fresh.BlizzardCharacterId;
        existing.LastSyncedAt = fresh.LastSyncedAt;

        return existing;
    }

    public async Task ReplaceEquipmentAsync(Guid characterId, CharacterEquipment fresh, CancellationToken ct)
    {
        var existing = await db.CharacterEquipments
            .Include(e => e.EquippedItems)
            .FirstOrDefaultAsync(e => e.CharacterId == characterId, ct);

        if (existing is null)
        {
            fresh.Id = Guid.NewGuid();
            fresh.CharacterId = characterId;

            foreach (var item in fresh.EquippedItems)
            {
                item.Id = Guid.NewGuid();
                item.CharacterEquipmentId = fresh.Id;
            }

            db.CharacterEquipments.Add(fresh);
            return;
        }

        existing.LastSyncedAt = fresh.LastSyncedAt;

        // Reconciled slot by slot rather than deleted and re-inserted. EquippedItem has a unique index
        // on (CharacterEquipmentId, Slot), and a delete plus an insert for the same slot inside one
        // SaveChanges depends on EF ordering the two statements favourably — which is not something to
        // rely on. Updating in place also means a refresh that changed nothing writes nothing.
        var bySlot = existing.EquippedItems.ToDictionary(item => item.Slot);

        foreach (var incoming in fresh.EquippedItems)
        {
            if (bySlot.Remove(incoming.Slot, out var current))
            {
                current.BlizzardItemId = incoming.BlizzardItemId;
                current.ItemName = incoming.ItemName;
                current.Quality = incoming.Quality;
                current.ItemLevel = incoming.ItemLevel;
                current.IconName = incoming.IconName;
                continue;
            }

            incoming.Id = Guid.NewGuid();
            incoming.CharacterEquipmentId = existing.Id;
            db.EquippedItems.Add(incoming);
        }

        // Whatever is left was equipped before and is not now — an emptied slot is a real change, and
        // leaving the old item there would show gear the character has taken off.
        db.EquippedItems.RemoveRange(bySlot.Values);
    }

    // The whole unit is handed in as a callback rather than exposing BeginTransactionAsync — see
    // DbContextTransactions, which now holds the single copy of that idiom (7.2b). This used to be one
    // of three.
    public Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation, CancellationToken ct) =>
        db.ExecuteInTransactionAsync(operation, ct);
}
