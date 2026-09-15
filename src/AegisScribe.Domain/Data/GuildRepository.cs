using AegisScribe.Domain.Managers.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace AegisScribe.Domain.Data;

public class GuildRepository(AegisScribeDbContext db) : IGuildRepository
{
    public Task<Guild?> FindAsync(Guid realmId, string guildName, CancellationToken ct)
    {
        var nameLower = guildName.ToLowerInvariant();

        return db.Guilds
            .AsNoTracking()
            .Include(g => g.Realm)
            .FirstOrDefaultAsync(g => g.RealmId == realmId && g.NameLower == nameLower, ct);
    }

    public Task<Guild?> FindByIdAsync(Guid guildId, CancellationToken ct) =>
        db.Guilds.AsNoTracking().Include(g => g.Realm).FirstOrDefaultAsync(g => g.Id == guildId, ct);

    public async Task<IReadOnlyList<Guild>> FindStaleAsync(DateTimeOffset staleBefore, int take, CancellationToken ct) =>
        await db.Guilds
            .AsNoTracking()
            .Include(g => g.Realm)
            .Where(g => g.LastSyncedAt < staleBefore)
            .OrderBy(g => g.LastSyncedAt)
            .Take(take)
            .ToListAsync(ct);

    public async Task<Guild> UpsertAsync(Guild fresh, Guid realmId, CancellationToken ct)
    {
        // By source id first, so a guild Blizzard has renamed moves rather than duplicating — the same
        // reasoning as Character and Realm. Then by natural key, which adopts a row seeded or created
        // before we knew the Blizzard id.
        var existing = fresh.BlizzardGuildId != 0
            ? await db.Guilds.FirstOrDefaultAsync(g => g.BlizzardGuildId == fresh.BlizzardGuildId, ct)
            : null;

        existing ??= await db.Guilds
            .FirstOrDefaultAsync(g => g.RealmId == realmId && g.NameLower == fresh.NameLower, ct);

        if (existing is null)
        {
            fresh.Id = Guid.NewGuid();
            fresh.RealmId = realmId;
            db.Guilds.Add(fresh);
            return fresh;
        }

        existing.RealmId = realmId;
        existing.Name = fresh.Name;
        existing.NameLower = fresh.NameLower;
        existing.Faction = fresh.Faction;
        existing.BlizzardGuildId = fresh.BlizzardGuildId;
        existing.LastSyncedAt = fresh.LastSyncedAt;

        return existing;
    }

    public async Task ReplaceMembersAsync(
        Guid guildId, IReadOnlyDictionary<Guid, int> ranksByCharacterId, CancellationToken ct)
    {
        var existing = await db.GuildMembers.Where(m => m.GuildId == guildId).ToListAsync(ct);

        // Reconciled in place rather than cleared and re-inserted, the same as EquippedItem: there is a
        // unique index on (GuildId, CharacterId), and a delete plus an insert for the same pair inside
        // one SaveChanges would depend on EF ordering the two statements favourably.
        var byCharacter = existing.ToDictionary(member => member.CharacterId);

        foreach (var (characterId, rank) in ranksByCharacterId)
        {
            if (byCharacter.Remove(characterId, out var current))
            {
                // A promotion or demotion is the common change; most members are unchanged and write
                // nothing at all.
                current.BlizzardRank = rank;
                continue;
            }

            db.GuildMembers.Add(new GuildMember
            {
                Id = Guid.NewGuid(),
                GuildId = guildId,
                CharacterId = characterId,
                BlizzardRank = rank,
            });
        }

        // Whoever is left was in the guild before and is not now — gquit, kicked, or transferred. The
        // GuildMember row goes; the global Character row stays, because the character still exists and
        // other guilds and communities may reference it.
        db.GuildMembers.RemoveRange(byCharacter.Values);
    }

    public Task<int> CountMembersAsync(Guid guildId, CancellationToken ct) =>
        db.GuildMembers.CountAsync(m => m.GuildId == guildId, ct);

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
