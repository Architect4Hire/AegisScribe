using AegisScribe.Domain.Data;
using AegisScribe.Domain.Managers.Models.ServiceModels;

namespace AegisScribe.Domain.Business;

public interface IGuildBusiness
{
    Task<IReadOnlyList<GuildServiceModel>> ListAsync(CancellationToken ct);

    // Links a guild to this community and syncs it in the same act. Null when Blizzard has no such
    // guild — nothing is linked in that case, so a typo does not leave a dead link behind.
    Task<GuildServiceModel?> LinkAsync(Guid tenantId, string region, string realmSlug, string guildName, CancellationToken ct);

    Task<GuildServiceModel?> ResyncAsync(Guid tenantId, Guid guildId, CancellationToken ct);

    Task<bool> UnlinkAsync(Guid guildId, CancellationToken ct);
}

// 6.6b. Tenant-triggered, so every path that reaches Blizzard charges the community's budget first.
public class GuildBusiness(
    ITenantSyncBudget budget,
    IGuildSyncDataLayer sync,
    IGuildRepository guilds,
    ITenantGuildRepository links,
    TimeProvider timeProvider) : IGuildBusiness
{
    // One call: the roster endpoint returns the guild AND every member together. Realm resolution can
    // add one per unknown realm, which after the catalogue sync is normally none — so the budget is
    // charged for the call this operation is actually about.
    private const int CallsPerGuildSync = 1;

    public async Task<IReadOnlyList<GuildServiceModel>> ListAsync(CancellationToken ct)
    {
        var linked = await links.ListAsync(ct);
        var models = new List<GuildServiceModel>(linked.Count);

        foreach (var link in linked)
        {
            models.Add(await ToServiceModelAsync(link.Guild, ct));
        }

        return models;
    }

    public async Task<GuildServiceModel?> LinkAsync(
        Guid tenantId, string region, string realmSlug, string guildName, CancellationToken ct)
    {
        // Charged before the call leaves, like every other tenant-triggered sync. Throws when the
        // budget is spent; the API turns that into a 429 with a retry hint.
        await budget.ConsumeAsync(tenantId, CallsPerGuildSync, ct);

        var guild = await sync.SyncAsync(region, realmSlug, guildName, ct);

        if (guild is null)
        {
            return null;
        }

        await links.LinkAsync(guild.Id, timeProvider.GetUtcNow(), ct);

        return await ToServiceModelAsync(guild, ct);
    }

    public async Task<GuildServiceModel?> ResyncAsync(Guid tenantId, Guid guildId, CancellationToken ct)
    {
        // Read through the tenant-scoped link, not the global guild: a community may only re-sync a
        // guild IT has linked. Reading the guild directly would let any officer spend their budget
        // refreshing a guild they have nothing to do with.
        var link = await links.FindAsync(guildId, ct);

        if (link is null)
        {
            return null;
        }

        await budget.ConsumeAsync(tenantId, CallsPerGuildSync, ct);

        var guild = await sync.SyncAsync(link.Guild.Realm.Region, link.Guild.Realm.Slug, link.Guild.Name, ct);

        return guild is null ? null : await ToServiceModelAsync(guild, ct);
    }

    public async Task<bool> UnlinkAsync(Guid guildId, CancellationToken ct)
    {
        if (await links.FindAsync(guildId, ct) is null)
        {
            return false;
        }

        await links.UnlinkAsync(guildId, ct);

        return true;
    }

    private async Task<GuildServiceModel> ToServiceModelAsync(Managers.Models.Domain.Guild guild, CancellationToken ct) =>
        new()
        {
            Id = guild.Id,
            Name = guild.Name,
            RealmSlug = guild.Realm?.Slug ?? string.Empty,
            Faction = guild.Faction,
            MemberCount = await guilds.CountMembersAsync(guild.Id, ct),
            LastSyncedAt = guild.LastSyncedAt,
        };
}
