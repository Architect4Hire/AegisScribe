using AegisScribe.Domain.Managers.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace AegisScribe.Domain.Data;

public class TenantGuildRepository(AegisScribeDbContext db) : ITenantGuildRepository
{
    public async Task<IReadOnlyList<TenantGuild>> ListAsync(CancellationToken ct) =>
        await db.TenantGuilds
            .AsNoTracking()
            .Include(link => link.Guild)
                .ThenInclude(guild => guild.Realm)
            .OrderBy(link => link.Guild.Name)
            .ToListAsync(ct);

    public Task<bool> AnyLinkedAsync(CancellationToken ct) => db.TenantGuilds.AnyAsync(ct);

    public Task<TenantGuild?> FindAsync(Guid guildId, CancellationToken ct) =>
        db.TenantGuilds
            .AsNoTracking()
            .Include(link => link.Guild)
                .ThenInclude(guild => guild.Realm)
            .FirstOrDefaultAsync(link => link.GuildId == guildId, ct);

    public async Task LinkAsync(Guid guildId, DateTimeOffset linkedAt, CancellationToken ct)
    {
        // The query filter makes this "does THIS tenant already link it", which is the question that
        // matters: two communities linking the same guild is normal and expected.
        if (await db.TenantGuilds.AnyAsync(link => link.GuildId == guildId, ct))
        {
            return;
        }

        db.TenantGuilds.Add(new TenantGuild
        {
            Id = Guid.NewGuid(),
            GuildId = guildId,
            LinkedAt = linkedAt,
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task UnlinkAsync(Guid guildId, CancellationToken ct)
    {
        // Removes this community's link only. The Guild row and its members stay — another community
        // may still follow it, and the data is global either way.
        await db.TenantGuilds.Where(link => link.GuildId == guildId).ExecuteDeleteAsync(ct);
    }
}
