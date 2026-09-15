using AegisScribe.Domain.Managers.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace AegisScribe.Domain.Data;

public class RealmRepository(AegisScribeDbContext db) : IRealmRepository
{
    public Task<Realm?> FindAsync(string region, string realmSlug, CancellationToken ct) =>
        db.Realms.AsNoTracking().FirstOrDefaultAsync(r => r.Region == region && r.Slug == realmSlug, ct);

    // Tracked, unlike FindAsync: the catalogue sync reconciles this set in memory and writes it back,
    // so the change tracker is doing the work of deciding which rows actually changed.
    public async Task<IReadOnlyList<Realm>> ListByRegionAsync(string region, CancellationToken ct) =>
        await db.Realms.Where(r => r.Region == region).ToListAsync(ct);

    public async Task<DateTimeOffset?> LatestSyncedAtAsync(string region, CancellationToken ct) =>
        await db.Realms
            .Where(r => r.Region == region)
            .OrderByDescending(r => r.LastSyncedAt)
            .Select(r => (DateTimeOffset?)r.LastSyncedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<Realm> UpsertAsync(Realm fresh, CancellationToken ct)
    {
        // By source id first. A realm Blizzard has renamed arrives with a slug we have never seen and
        // an id we already hold, and matching on the slug would insert a second row while leaving every
        // character pointed at the old one.
        var existing = fresh.BlizzardRealmId != 0
            ? await db.Realms.FirstOrDefaultAsync(r => r.BlizzardRealmId == fresh.BlizzardRealmId, ct)
            : null;

        // Then by natural key, which is what adopts a row written before the catalogue sync existed —
        // a seeded realm, or one resolved lazily by 6.4's character read. Without this the insert below
        // would violate the unique index on (Region, Slug) instead of filling in the id.
        existing ??= await db.Realms
            .FirstOrDefaultAsync(r => r.Region == fresh.Region && r.Slug == fresh.Slug, ct);

        return Apply(existing, fresh);
    }

    public async Task<int> UpsertCatalogueAsync(string region, IReadOnlyList<Realm> fresh, CancellationToken ct)
    {
        // Tracked on purpose: the change tracker is what decides which of these rows actually changed,
        // so a pass that confirmed 250 unchanged realms writes 250 LastSyncedAt values and nothing else.
        var existing = await db.Realms.Where(r => r.Region == region).ToListAsync(ct);

        var bySourceId = existing
            .Where(realm => realm.BlizzardRealmId != 0)
            .ToDictionary(realm => realm.BlizzardRealmId);

        var bySlug = existing.ToDictionary(realm => (realm.Region, realm.Slug));

        var inserted = 0;

        foreach (var incoming in fresh)
        {
            Realm? match = null;

            if (incoming.BlizzardRealmId != 0)
            {
                bySourceId.TryGetValue(incoming.BlizzardRealmId, out match);
            }

            match ??= bySlug.GetValueOrDefault((incoming.Region, incoming.Slug));

            if (match is null)
            {
                inserted++;
            }

            Apply(match, incoming);
        }

        return inserted;
    }

    private Realm Apply(Realm? existing, Realm fresh)
    {
        if (existing is null)
        {
            fresh.Id = Guid.NewGuid();
            db.Realms.Add(fresh);
            return fresh;
        }

        existing.Region = fresh.Region;
        existing.Slug = fresh.Slug;
        existing.Name = fresh.Name;
        existing.BlizzardRealmId = fresh.BlizzardRealmId;
        existing.BlizzardConnectedRealmId = fresh.BlizzardConnectedRealmId;

        // Always advanced, even when nothing else changed. This column is the 30-day refresh clock the
        // Terms of Use are enforced against, not a change marker — a pass that confirmed a realm is
        // unchanged has still refreshed it.
        existing.LastSyncedAt = fresh.LastSyncedAt;

        return existing;
    }

    // Same shape and same reason as CharacterRepository's: Aspire's execution strategy will not run
    // inside a caller-opened transaction, so the whole unit is handed in (backend.md).
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
