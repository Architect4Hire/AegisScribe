using System.Text.Json;
using AegisScribe.Domain.Business;
using AegisScribe.Domain.Context;
using AegisScribe.Domain.Facade;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;
using AegisScribe.Domain.Managers.Validators;
using FluentValidation;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace AegisScribe.Tests.Roster;

// Facade: real validator, mocked business, a real in-memory IDistributedCache.
//
// The trio (hit / miss / validation failure) plus invalidation, and one assertion this facade owes
// that the rank facade does not: the cached payload must be USER-INDEPENDENT. This cache is keyed by
// tenant, so anything user-specific in it is computed for the first caller and then served to every
// other member of the community.
public class CharacterClaimFacadeTests
{
    private static readonly Guid TenantId = Guid.Parse("3f1c9d2e-7b45-4a86-9e13-5c8a2d4f6b70");
    private static readonly Guid CharacterId = Guid.Parse("b71e4a0c-2d38-4f59-8a6b-0c1d2e3f4a5b");
    private static readonly string ClaimKey = $"t:{TenantId}:claim:{CharacterId}";

    private readonly ICharacterClaimBusiness _business = Substitute.For<ICharacterClaimBusiness>();
    private readonly IDistributedCache _cache = CreateCache();
    private readonly CharacterClaimFacade _facade;

    public CharacterClaimFacadeTests()
    {
        _facade = CreateFacade(_cache);
    }

    private CharacterClaimFacade CreateFacade(IDistributedCache cache)
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(TenantId);

        return new CharacterClaimFacade(
            _business, new ClaimCharacterViewModelValidator(), cache, tenantContext);
    }

    private static IDistributedCache CreateCache()
    {
        var services = new ServiceCollection();
        services.AddDistributedMemoryCache();
        return services.BuildServiceProvider().GetRequiredService<IDistributedCache>();
    }

    [Fact]
    public async Task Get_CacheMiss_CallsBusiness_AndPopulatesTheTenantPrefixedKey()
    {
        _business.GetAsync(CharacterId, Arg.Any<CancellationToken>()).Returns(
            new CharacterClaimServiceModel { CharacterId = CharacterId, ClaimedByUserId = "user-1" });

        var result = await _facade.GetAsync(CharacterId, CancellationToken.None);

        Assert.Equal("user-1", result.ClaimedByUserId);
        await _business.Received(1).GetAsync(CharacterId, Arg.Any<CancellationToken>());

        // Prefixed with the tenant, and nothing under a bare key.
        Assert.NotNull(await _cache.GetAsync(ClaimKey, CancellationToken.None));
        Assert.Null(await _cache.GetAsync($"claim:{CharacterId}", CancellationToken.None));
    }

    [Fact]
    public async Task Get_CacheHit_NeverCallsBusiness()
    {
        await SeedCachedAsync("user-cached");

        var result = await _facade.GetAsync(CharacterId, CancellationToken.None);

        Assert.Equal("user-cached", result.ClaimedByUserId);
        await _business.DidNotReceiveWithAnyArgs().GetAsync(default, default);
    }

    [Fact]
    public async Task Get_CachedPayload_IsUserIndependent()
    {
        // The hazard this response shape was designed around. If the model ever gains an `isMine`-style
        // field, the first caller's answer is what every other member of the community then reads —
        // correctly tenant-prefixed and still wrong. Pinned here so that change fails a test rather
        // than a user.
        _business.GetAsync(CharacterId, Arg.Any<CancellationToken>()).Returns(
            new CharacterClaimServiceModel { CharacterId = CharacterId, ClaimedByUserId = "user-1" });

        await _facade.GetAsync(CharacterId, CancellationToken.None);

        var cached = await _cache.GetAsync(ClaimKey, CancellationToken.None);
        var json = JsonSerializer.Deserialize<JsonElement>(cached!);

        Assert.Equal(
            new[] { "CharacterId", "ClaimedAt", "ClaimedByDisplayName", "ClaimedByUserId" },
            json.EnumerateObject().Select(p => p.Name).Order().ToArray());
    }

    [Fact]
    public async Task Get_Unclaimed_IsCachedAsAPopulatedModelWithNulls()
    {
        // Unlike CharacterFacade's deliberately-uncached not-found, "nobody has claimed this" is a
        // settled answer — and every write below removes the key the moment it stops being true.
        _business.GetAsync(CharacterId, Arg.Any<CancellationToken>()).Returns((CharacterClaimServiceModel?)null);

        var result = await _facade.GetAsync(CharacterId, CancellationToken.None);

        Assert.Equal(CharacterId, result.CharacterId);
        Assert.Null(result.ClaimedByUserId);
        Assert.NotNull(await _cache.GetAsync(ClaimKey, CancellationToken.None));
    }

    [Fact]
    public async Task Claim_Invalid_ThrowsValidationException_AndNeverReachesBusinessOrCache()
    {
        var mockCache = Substitute.For<IDistributedCache>();
        var facade = CreateFacade(mockCache);

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => facade.ClaimAsync(new ClaimCharacterViewModel { CharacterId = Guid.Empty }, CancellationToken.None));

        Assert.Contains(ex.Errors, e => e.PropertyName == nameof(ClaimCharacterViewModel.CharacterId));
        await _business.DidNotReceiveWithAnyArgs().ClaimAsync(default!, default);
        await mockCache.DidNotReceiveWithAnyArgs().RemoveAsync(default!, default);
    }

    [Fact]
    public async Task Claim_InvalidatesTheCachedState()
    {
        await SeedCachedAsync("user-stale");

        await _facade.ClaimAsync(new ClaimCharacterViewModel { CharacterId = CharacterId }, CancellationToken.None);

        Assert.Null(await _cache.GetAsync(ClaimKey, CancellationToken.None));
    }

    [Fact]
    public async Task Release_InvalidatesTheCachedState()
    {
        await SeedCachedAsync("user-stale");

        await _facade.ReleaseAsync(CharacterId, CancellationToken.None);

        Assert.Null(await _cache.GetAsync(ClaimKey, CancellationToken.None));
    }

    [Fact]
    public async Task Clear_InvalidatesTheCachedState()
    {
        await SeedCachedAsync("user-stale");

        await _facade.ClearAsync(CharacterId, CancellationToken.None);

        Assert.Null(await _cache.GetAsync(ClaimKey, CancellationToken.None));
    }

    private Task SeedCachedAsync(string userId) =>
        _cache.SetAsync(
            ClaimKey,
            JsonSerializer.SerializeToUtf8Bytes(new CharacterClaimServiceModel
            {
                CharacterId = CharacterId,
                ClaimedByUserId = userId,
            }),
            new DistributedCacheEntryOptions(),
            CancellationToken.None);
}
