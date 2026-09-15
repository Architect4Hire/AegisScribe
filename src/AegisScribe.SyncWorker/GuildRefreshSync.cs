using AegisScribe.Domain.Data;
using AegisScribe.Domain.Integration.Blizzard;
using Microsoft.Extensions.Options;

namespace AegisScribe.SyncWorker;

// Keeps linked guild rosters inside the Terms of Use thirty-day window.
//
// It never iterates tenants and does not have to: Guild is GLOBAL, and a row exists only because some
// community linked it, so "every guild anybody follows" is simply "every Guild row". Iterating tenants
// would refetch a shared guild once per follower.
//
// Background work draws no tenant budget — that bounds work a community ASKED for.
public sealed class GuildRefreshSync(
    IServiceScopeFactory scopeFactory,
    IBlizzardStalenessPolicy staleness,
    IOptions<SyncWorkerOptions> options,
    ILogger<GuildRefreshSync> logger)
{
    public async Task<GuildRefreshResult> RunAsync(CancellationToken ct)
    {
        var settings = options.Value;
        IReadOnlyList<Domain.Managers.Models.Domain.Guild> due;

        using (var scope = scopeFactory.CreateScope())
        {
            if (!scope.ServiceProvider.GetRequiredService<IBlizzardGateway>().IsConfigured)
            {
                logger.LogInformation("Blizzard credentials are not configured; skipping the guild refresh pass.");
                return GuildRefreshResult.Skipped;
            }

            due = await scope.ServiceProvider
                .GetRequiredService<IGuildRepository>()
                .FindStaleAsync(staleness.StaleBefore(), settings.GuildBatchSize, ct);
        }

        if (due.Count == 0)
        {
            logger.LogInformation("No guild rosters are past the refresh window.");
            return GuildRefreshResult.Skipped;
        }

        var refreshed = 0;
        var failed = 0;
        var gate = new Lock();

        // Bounded, never Task.WhenAll. One call per guild makes this far cheaper than the character
        // pass, but a hundred guilds all in flight would still queue a hundred leases ahead of every
        // user request.
        await Parallel.ForEachAsync(
            due,
            new ParallelOptions { MaxDegreeOfParallelism = settings.MaxConcurrentRefreshes, CancellationToken = ct },
            async (guild, token) =>
            {
                var ok = await RefreshOneAsync(guild, token);

                lock (gate)
                {
                    if (ok)
                    {
                        refreshed++;
                    }
                    else
                    {
                        failed++;
                    }
                }
            });

        logger.LogInformation(
            "Guild refresh pass: {Refreshed} refreshed, {Failed} failed, out of {Selected} selected.",
            refreshed,
            failed,
            due.Count);

        return new GuildRefreshResult(due.Count, refreshed, failed, Ran: true);
    }

    private async Task<bool> RefreshOneAsync(Domain.Managers.Models.Domain.Guild guild, CancellationToken ct)
    {
        // A scope per guild: the sync below holds a DbContext, and this loop runs several at once.
        using var scope = scopeFactory.CreateScope();

        try
        {
            var synced = await scope.ServiceProvider
                .GetRequiredService<IGuildSyncDataLayer>()
                .SyncAsync(guild.Realm.Region, guild.Realm.Slug, guild.Name, ct);

            if (synced is null)
            {
                // Blizzard no longer knows this guild — disbanded, renamed, or transferred. The row is
                // left alone and stays due, exactly as a 404 on a character does: removing other
                // people's data on the strength of a 404 is the erasure routine's decision, not a
                // sync job's.
                logger.LogWarning(
                    "Blizzard no longer knows guild {GuildName} on {RealmSlug}.", guild.Name, guild.Realm.Slug);

                return false;
            }

            return true;
        }
        catch (BlizzardUnavailableException exception)
        {
            // One guild failing does not fail the pass; the row stays stale and the next pass retries.
            logger.LogWarning(exception, "Could not refresh guild {GuildName}; it stays due.", guild.Name);

            return false;
        }
    }
}

public sealed record GuildRefreshResult(int Selected, int Refreshed, int Failed, bool Ran)
{
    public static readonly GuildRefreshResult Skipped = new(0, 0, 0, Ran: false);
}
