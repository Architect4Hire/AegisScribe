using System.Text.Json;
using AegisScribe.Domain.Business;
using AegisScribe.Domain.Context;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;
using FluentValidation;
using Microsoft.Extensions.Caching.Distributed;

namespace AegisScribe.Domain.Facade;

// Cached under t:{tenantId}:guild-rank-names via TenantScopedFacadeBase, so no bare key is expressible
// here (tenancy.md). Safe to cache, unlike the roster page: one small set per community, nothing
// per-caller. Near-static — a guild renames its ranks about never — so the TTL is long, with the write
// below removing the key the moment it stops being true.
public class GuildRankNameFacade(
    IGuildRankNameBusiness business,
    IValidator<SetGuildRankNameViewModel> setValidator,
    IDistributedCache cache,
    ITenantContext tenantContext) : TenantScopedFacadeBase(tenantContext), IGuildRankNameFacade
{
    private static readonly DistributedCacheEntryOptions CacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1),
    };

    private const string CacheEntryKey = "guild-rank-names";

    public async Task<IReadOnlyList<GuildRankNameServiceModel>> ListAsync(CancellationToken ct)
    {
        var key = CacheKey(CacheEntryKey);
        var cached = await cache.GetAsync(key, ct);

        if (cached is not null)
        {
            try
            {
                return JsonSerializer.Deserialize<List<GuildRankNameServiceModel>>(cached)!;
            }
            catch (JsonException)
            {
                // A shape-incompatible entry falls through to the source rather than failing the
                // request, as CharacterFacade does.
            }
        }

        var names = await business.ListAsync(ct);

        await cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(names), CacheOptions, ct);

        return names;
    }

    public async Task<bool> SetAsync(
        Guid guildId, int rank, SetGuildRankNameViewModel viewModel, CancellationToken ct)
    {
        await setValidator.ValidateAndThrowAsync(viewModel, ct);

        var set = await business.SetAsync(guildId, rank, viewModel, ct);

        // Invalidated even when the guild was not followed: one unnecessary Redis delete costs nothing
        // next to a branch that could get the "we changed something" case wrong later.
        await cache.RemoveAsync(CacheKey(CacheEntryKey), ct);

        return set;
    }
}
