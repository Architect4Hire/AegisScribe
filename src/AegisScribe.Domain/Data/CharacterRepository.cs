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

    // The Aspire-enabled execution strategy refuses to run inside a caller-opened transaction
    // (backend.md), so the whole unit is handed in as a callback rather than exposing
    // BeginTransactionAsync. No shared base class for this yet — every repository defines its own
    // copy (see TenantRepository).
    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var result = await operation(ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return result;
        });
    }
}
