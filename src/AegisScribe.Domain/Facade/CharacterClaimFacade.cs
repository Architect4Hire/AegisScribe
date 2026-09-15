using System.Text.Json;
using AegisScribe.Domain.Business;
using AegisScribe.Domain.Context;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;
using FluentValidation;
using Microsoft.Extensions.Caching.Distributed;

namespace AegisScribe.Domain.Facade;

// Caches claim state under t:{tenantId}:claim:{characterId}, via TenantScopedFacadeBase so no bare key
// is expressible here (tenancy.md).
//
// What makes that safe is a property of CharacterClaimServiceModel rather than of this class: the
// payload is USER-INDEPENDENT. It reports who holds the claim, not whether the caller does, so one
// member's read is a correct answer for every other member of the community. An `isMine` flag would
// have been cached for the first caller and then served to everyone — correctly prefixed and still
// wrong. If a per-user field is ever added to that model, this cache has to go or become per-user.
public class CharacterClaimFacade(
    ICharacterClaimBusiness business,
    IValidator<ClaimCharacterViewModel> claimValidator,
    IDistributedCache cache,
    ITenantContext tenantContext) : TenantScopedFacadeBase(tenantContext), ICharacterClaimFacade
{
    // Minutes, for page-load latency (the add-endpoint skill's cache table). Short even by that
    // standard because claiming is a visible, immediate action — a member who claims a character and
    // sees it still unclaimed on the next screen will claim it again.
    private static readonly DistributedCacheEntryOptions CacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
    };

    public async Task<CharacterClaimServiceModel> GetAsync(Guid characterId, CancellationToken ct)
    {
        var key = ClaimKey(characterId);
        var cached = await cache.GetAsync(key, ct);

        if (cached is not null)
        {
            try
            {
                return JsonSerializer.Deserialize<CharacterClaimServiceModel>(cached)!;
            }
            catch (JsonException)
            {
                // A shape-incompatible entry (mid-deploy, the ServiceModel changed) falls through to
                // the source rather than failing the request, as CharacterFacade does.
            }
        }

        // "Unclaimed" is a real answer here, not a miss — unlike CharacterFacade's not-found, which is
        // deliberately uncached. It is cached for the same reason it is returned as a populated model
        // with null fields: an unclaimed character is a settled state, and every write below removes
        // this key the moment it changes.
        var claim = await business.GetAsync(characterId, ct)
            ?? new CharacterClaimServiceModel { CharacterId = characterId };

        await cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(claim), CacheOptions, ct);

        return claim;
    }

    public async Task<CharacterClaimServiceModel?> ClaimAsync(
        ClaimCharacterViewModel viewModel, CancellationToken ct)
    {
        await claimValidator.ValidateAndThrowAsync(viewModel, ct);

        var claim = await business.ClaimAsync(viewModel, ct);

        await InvalidateAsync(viewModel.CharacterId, ct);

        return claim;
    }

    public async Task ReleaseAsync(Guid characterId, CancellationToken ct)
    {
        await business.ReleaseAsync(characterId, ct);

        await InvalidateAsync(characterId, ct);
    }

    public async Task ClearAsync(Guid characterId, CancellationToken ct)
    {
        await business.ClearAsync(characterId, ct);

        await InvalidateAsync(characterId, ct);
    }

    private string ClaimKey(Guid characterId) => CacheKey($"claim:{characterId}");

    private Task InvalidateAsync(Guid characterId, CancellationToken ct) =>
        cache.RemoveAsync(ClaimKey(characterId), ct);
}
