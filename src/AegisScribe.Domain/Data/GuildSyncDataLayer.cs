using AegisScribe.Domain.Integration.Blizzard;
using AegisScribe.Domain.Managers.Models.Domain;
using Microsoft.Extensions.Logging;

namespace AegisScribe.Domain.Data;

public interface IGuildSyncDataLayer
{
    // Fetches a guild roster and reconciles it into the store. Null when Blizzard has no such guild.
    //
    // Returns the persisted Guild so a caller can report what it linked without a second read.
    Task<Guild?> SyncAsync(string region, string realmSlug, string guildName, CancellationToken ct);
}

// One Blizzard call in; a guild, its members and their characters out.
//
// Fetch everything first, then transact, for the same reason as every other sync here:
// ExecuteInTransactionAsync hands the unit to EF's execution strategy, which may run it more than once,
// and an HTTP call inside would fire again on every retry.
public class GuildSyncDataLayer(
    IBlizzardGateway gateway,
    IGuildRepository guilds,
    IRealmRepository realms,
    ICharacterRepository characters,
    ILogger<GuildSyncDataLayer> logger) : IGuildSyncDataLayer
{
    public async Task<Guild?> SyncAsync(string region, string realmSlug, string guildName, CancellationToken ct)
    {
        // ONE call for the guild and every member: the roster response carries the guild object too, so
        // linking does not cost a separate guild-summary call.
        var snapshot = await gateway.FetchGuildRosterAsync(realmSlug, Slugify(guildName), ct);

        if (snapshot is null)
        {
            return null;
        }

        // Members can sit on several realms — connected realms share a roster. Each unknown realm costs
        // one call, and after the catalogue pass there are usually none.
        var realmIds = await ResolveRealmsAsync(region, realmSlug, snapshot, ct);

        if (!realmIds.TryGetValue(realmSlug.ToLowerInvariant(), out var guildRealmId))
        {
            logger.LogWarning(
                "Could not resolve realm {RealmSlug} for guild {GuildName}; the guild cannot be stored.",
                realmSlug,
                guildName);

            return null;
        }

        // Every fetch is finished. From here it is one atomic unit: the guild, the characters it needs
        // to point at, and the membership diff either all land or none do.
        return await guilds.ExecuteInTransactionAsync(
            async token =>
            {
                var guild = await guilds.UpsertAsync(snapshot.Guild, guildRealmId, token);
                var ranks = new Dictionary<Guid, int>();

                foreach (var membership in snapshot.Members)
                {
                    if (!realmIds.TryGetValue(membership.RealmSlug.ToLowerInvariant(), out var memberRealmId))
                    {
                        // A member on a realm we could not resolve. Skipped rather than failing the
                        // roster — the same call the mapper makes for a member it cannot represent.
                        continue;
                    }

                    // Creates the Character row on first sight. The roster carries enough to do that —
                    // name, id, realm, level, class, faction — which is what makes a whole roster
                    // storable from one call. Item level and gear wait for the refresh worker.
                    var character = await characters.UpsertCharacterAsync(membership.Character, memberRealmId, token);

                    ranks[character.Id] = membership.BlizzardRank;
                }

                await guilds.ReplaceMembersAsync(guild.Id, ranks, token);

                return guild;
            },
            ct);
    }

    private async Task<Dictionary<string, Guid>> ResolveRealmsAsync(
        string region, string realmSlug, GuildRosterSnapshot snapshot, CancellationToken ct)
    {
        var slugs = snapshot.Members
            .Select(member => member.RealmSlug.ToLowerInvariant())
            .Append(realmSlug.ToLowerInvariant())
            .Distinct()
            .ToList();

        var resolved = new Dictionary<string, Guid>();

        foreach (var slug in slugs)
        {
            var realm = await realms.FindAsync(region, slug, ct);

            if (realm is not null)
            {
                resolved[slug] = realm.Id;
                continue;
            }

            // Sequential, not parallel: a roster spans a handful of realms at most.
            var fetched = await gateway.FetchRealmAsync(region, slug, ct);

            if (fetched is null)
            {
                logger.LogWarning("Blizzard does not know realm {RealmSlug}; its members will be skipped.", slug);
                continue;
            }

            var persisted = await realms.ExecuteInTransactionAsync(
                token => realms.UpsertAsync(fetched, token), ct);

            resolved[slug] = persisted.Id;
        }

        return resolved;
    }

    // Blizzard addresses a guild by a slug: lowercased, spaces as hyphens. "Ashes of Dawn" is
    // "ashes-of-dawn". Getting this wrong is a 404 that reads as "no such guild".
    private static string Slugify(string guildName) =>
        guildName.Trim().ToLowerInvariant().Replace(' ', '-');
}
