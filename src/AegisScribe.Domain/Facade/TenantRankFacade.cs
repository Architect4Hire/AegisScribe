using System.Text.Json;
using AegisScribe.Domain.Business;
using AegisScribe.Domain.Context;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;
using FluentValidation;
using Microsoft.Extensions.Caching.Distributed;

namespace AegisScribe.Domain.Facade;

// The first facade in the repo to actually cache a tenant-scoped ServiceModel, which is why it
// inherits TenantScopedFacadeBase rather than building a key by hand: CacheKey(...) is prefixed with
// the ambient tenant and there is no overload that isn't, so a bare key that would serve one
// community's ladder to another is not expressible here (tenancy.md).
public class TenantRankFacade(
    ITenantRankBusiness business,
    IValidator<CreateRankViewModel> createValidator,
    IValidator<UpdateRankViewModel> updateValidator,
    IDistributedCache cache,
    ITenantContext tenantContext) : TenantScopedFacadeBase(tenantContext), ITenantRankFacade
{
    // The long end of the add-endpoint skill's cache table (60 minutes, "static reference data"),
    // which a rank ladder genuinely is — it changes when an officer edits it and not otherwise. The
    // TTL is the backstop, not the correctness mechanism: every write below removes the key, and
    // Redis is shared across instances, so a stale ladder cannot outlive the edit that changed it.
    private static readonly DistributedCacheEntryOptions CacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1),
    };

    private const string RanksCacheKey = "ranks";

    public async Task<IReadOnlyList<TenantRankServiceModel>> ListAsync(CancellationToken ct)
    {
        var key = CacheKey(RanksCacheKey);
        var cached = await cache.GetAsync(key, ct);

        if (cached is not null)
        {
            try
            {
                return JsonSerializer.Deserialize<List<TenantRankServiceModel>>(cached)!;
            }
            catch (JsonException)
            {
                // A shape-incompatible entry (mid-deploy, the ServiceModel changed) falls through to
                // the source rather than failing the request, the same way CharacterFacade does.
            }
        }

        var ranks = await business.ListAsync(ct);

        // An empty ladder IS cached, unlike CharacterFacade's deliberate not-found miss: a community
        // that has not defined any ranks yet is a settled answer, and the create path below removes
        // this key the moment that changes.
        await cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(ranks), CacheOptions, ct);

        return ranks;
    }

    public async Task<TenantRankServiceModel> CreateAsync(CreateRankViewModel viewModel, CancellationToken ct)
    {
        await createValidator.ValidateAndThrowAsync(viewModel, ct);

        var rank = await business.CreateAsync(viewModel, ct);

        await InvalidateAsync(ct);

        return rank;
    }

    public async Task<TenantRankServiceModel?> UpdateAsync(
        Guid rankId, UpdateRankViewModel viewModel, CancellationToken ct)
    {
        await updateValidator.ValidateAndThrowAsync(viewModel, ct);

        var rank = await business.UpdateAsync(rankId, viewModel, ct);

        // Invalidated even when the rank was not found: the cost of one unnecessary Redis delete is
        // nothing next to a branch that could get the "we changed something" case wrong later.
        await InvalidateAsync(ct);

        return rank;
    }

    public async Task DeleteAsync(Guid rankId, CancellationToken ct)
    {
        await business.DeleteAsync(rankId, ct);

        await InvalidateAsync(ct);
    }

    private Task InvalidateAsync(CancellationToken ct) => cache.RemoveAsync(CacheKey(RanksCacheKey), ct);
}
