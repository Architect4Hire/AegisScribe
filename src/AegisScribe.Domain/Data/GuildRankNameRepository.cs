using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using Microsoft.EntityFrameworkCore;

namespace AegisScribe.Domain.Data;

public class GuildRankNameRepository(AegisScribeDbContext db) : IGuildRankNameRepository
{
    // The range Blizzard's guild roster reports. Fixed by the game, not by us.
    private const int LowestRank = 0;
    private const int HighestRank = 9;

    public async Task<IReadOnlyList<GuildRankNameServiceModel>> ListAsync(CancellationToken ct)
    {
        // The guilds this community follows, through the tenant-scoped link rather than the global
        // Guild table — an officer names the ranks of guilds they actually follow and no others.
        var guilds = await db.TenantGuilds
            .AsNoTracking()
            .Select(link => new { link.GuildId, link.Guild.Name })
            .OrderBy(guild => guild.Name)
            .ToListAsync(ct);

        var stored = await db.GuildRankNames
            .AsNoTracking()
            .Select(row => new { row.GuildId, row.Rank, row.Name })
            .ToListAsync(ct);

        var byKey = stored.ToDictionary(row => (row.GuildId, row.Rank), row => row.Name);

        // Ten rows per guild whether or not they are stored: the editor is a form over a fixed range,
        // so an unnamed rank is a blank field to fill in rather than a row that is missing.
        return
        [
            .. from guild in guilds
               from rank in Enumerable.Range(LowestRank, HighestRank - LowestRank + 1)
               select new GuildRankNameServiceModel
               {
                   GuildId = guild.GuildId,
                   GuildName = guild.Name,
                   Rank = rank,
                   Name = byKey.GetValueOrDefault((guild.GuildId, rank)),
               },
        ];
    }

    public Task<bool> FollowsGuildAsync(Guid guildId, CancellationToken ct) =>
        db.TenantGuilds.AnyAsync(link => link.GuildId == guildId, ct);

    public async Task SetAsync(Guid guildId, int rank, string? name, CancellationToken ct)
    {
        var existing = await db.GuildRankNames
            .FirstOrDefaultAsync(row => row.GuildId == guildId && row.Rank == rank, ct);

        if (string.IsNullOrWhiteSpace(name))
        {
            // Cleared. The row goes rather than holding an empty string, so "never named" and "named
            // then cleared" are the same state — the UI has one blank to render, not two.
            if (existing is not null)
            {
                db.GuildRankNames.Remove(existing);
                await db.SaveChangesAsync(ct);
            }

            return;
        }

        if (existing is null)
        {
            db.GuildRankNames.Add(new GuildRankName
            {
                Id = Guid.NewGuid(),
                // TenantId is stamped by the SaveChanges interceptor, never assigned here (tenancy.md).
                GuildId = guildId,
                Rank = rank,
                Name = name,
            });
        }
        else
        {
            existing.Name = name;
        }

        await db.SaveChangesAsync(ct);
    }
}
