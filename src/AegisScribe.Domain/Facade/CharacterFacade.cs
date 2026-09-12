using System.Text.Json;
using AegisScribe.Domain.Business;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;
using FluentValidation;
using Microsoft.Extensions.Caching.Distributed;

namespace AegisScribe.Domain.Facade;

// Character is global reference data (tenancy.md) — GlobalFacadeBase.CacheKey(...) builds a bare key,
// never tenant-prefixed, so the cache stays shared across every community instead of fragmenting N ways.
public class CharacterFacade(
    ICharacterBusiness business,
    IValidator<CharacterLookupViewModel> lookupValidator,
    IValidator<SearchCharactersViewModel> searchValidator,
    IDistributedCache cache) : GlobalFacadeBase, ICharacterFacade
{
    // Character detail TTL per the add-endpoint skill's cache table — minutes, for page-load latency,
    // distinct from the SQL store's own 30-day-capped staleness window (that's the DataLayer's job).
    private static readonly DistributedCacheEntryOptions CacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
    };

    public async Task<CharacterDetailServiceModel?> GetCharacterAsync(CharacterLookupViewModel viewModel, CancellationToken ct)
    {
        await lookupValidator.ValidateAndThrowAsync(viewModel, ct);

        var key = CacheKey($"char:{viewModel.Region}:{viewModel.RealmSlug}:{viewModel.Name.ToLowerInvariant()}");
        var cached = await cache.GetAsync(key, ct);
        if (cached is not null)
        {
            try
            {
                return JsonSerializer.Deserialize<CharacterDetailServiceModel>(cached);
            }
            catch (JsonException)
            {
                // A shape-incompatible entry (e.g. mid-deploy, the ServiceModel changed) falls through
                // to the source below instead of failing the request — the 5-minute TTL self-heals it.
            }
        }

        var result = await business.GetCharacterAsync(viewModel.Region, viewModel.RealmSlug, viewModel.Name, ct);

        // A miss is not cached: caching "not found" risks serving a stale absence for the TTL window
        // if a sync lands moments later. Only a found character is worth the read-through.
        if (result is not null)
        {
            await cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(result), CacheOptions, ct);
        }

        return result;
    }

    // Not cached: a list read's key would have to include the name filter, cursor and limit, which is
    // unbounded cardinality for a page that's cheap to recompute — unlike the single-entity detail key
    // above. Still validated, since Limit carries the server-enforced max api-contract.md requires.
    public async Task<IReadOnlyList<CharacterSummaryServiceModel>> SearchCharactersAsync(SearchCharactersViewModel viewModel, CancellationToken ct)
    {
        await searchValidator.ValidateAndThrowAsync(viewModel, ct);
        return await business.SearchCharactersAsync(
            viewModel.Region, viewModel.RealmSlug, viewModel.Name, viewModel.AfterNameLower, viewModel.AfterId, viewModel.Limit, ct);
    }
}
