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

namespace AegisScribe.Tests.Tenancy;

// Facade: real validators, mocked business, a real in-memory IDistributedCache (add-endpoint skill's
// hit / miss / validation-failure trio, plus the invalidation this facade owes because it writes).
//
// The tenancy-specific assertion these carry over CharacterFacadeTests is the KEY SHAPE: this is the
// first facade caching tenant-scoped data, and a bare key here would serve one community's ladder to
// another (tenancy.md).
public class TenantRankFacadeTests
{
    private static readonly Guid TenantId = Guid.Parse("8e2b0f1a-2c3d-4e5f-9a7b-1c2d3e4f5a6b");
    private static readonly string TenantKey = $"t:{TenantId}:ranks";

    private readonly ITenantRankBusiness _business = Substitute.For<ITenantRankBusiness>();
    private readonly IDistributedCache _cache = CreateCache();
    private readonly TenantRankFacade _facade;

    public TenantRankFacadeTests()
    {
        _facade = CreateFacade(_cache);
    }

    private TenantRankFacade CreateFacade(IDistributedCache cache)
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(TenantId);

        return new TenantRankFacade(
            _business,
            new CreateRankViewModelValidator(),
            new UpdateRankViewModelValidator(),
            cache,
            tenantContext);
    }

    private static IDistributedCache CreateCache()
    {
        var services = new ServiceCollection();
        services.AddDistributedMemoryCache();
        return services.BuildServiceProvider().GetRequiredService<IDistributedCache>();
    }

    [Fact]
    public async Task List_CacheMiss_CallsBusiness_AndPopulatesTheTenantPrefixedKey()
    {
        var expected = new List<TenantRankServiceModel>
        {
            new() { Id = Guid.NewGuid(), Name = "Raider", SortOrder = 10, Colour = "#cba76a" },
        };
        _business.ListAsync(Arg.Any<CancellationToken>()).Returns(expected);

        var result = await _facade.ListAsync(CancellationToken.None);

        Assert.Equal(expected[0].Id, result[0].Id);
        await _business.Received(1).ListAsync(Arg.Any<CancellationToken>());

        // The assertion this whole file exists for: the key starts with the tenant, and reading the
        // bare key back finds nothing. A regression to a global-shaped key fails both halves.
        var cachedBytes = await _cache.GetAsync(TenantKey, CancellationToken.None);
        Assert.NotNull(cachedBytes);
        Assert.Equal("Raider", JsonSerializer.Deserialize<List<TenantRankServiceModel>>(cachedBytes!)![0].Name);
        Assert.Null(await _cache.GetAsync("ranks", CancellationToken.None));
    }

    [Fact]
    public async Task List_CacheHit_NeverCallsBusiness()
    {
        var cached = new List<TenantRankServiceModel>
        {
            new() { Id = Guid.NewGuid(), Name = "Trial", SortOrder = 20, Colour = "#616d7e" },
        };
        await _cache.SetAsync(
            TenantKey,
            JsonSerializer.SerializeToUtf8Bytes(cached),
            new DistributedCacheEntryOptions(),
            CancellationToken.None);

        var result = await _facade.ListAsync(CancellationToken.None);

        Assert.Equal("Trial", result[0].Name);
        await _business.DidNotReceiveWithAnyArgs().ListAsync(default);
    }

    [Fact]
    public async Task List_AnotherTenantsCacheEntry_IsNotServed()
    {
        // The same logical key under a different tenant's prefix. If CacheKey(...) ever lost its
        // prefix, this entry would be returned here and the two communities would share a ladder.
        var otherTenantEntry = new List<TenantRankServiceModel>
        {
            new() { Id = Guid.NewGuid(), Name = "SomebodyElsesRank", SortOrder = 0, Colour = "#ffffff" },
        };
        await _cache.SetAsync(
            $"t:{Guid.NewGuid()}:ranks",
            JsonSerializer.SerializeToUtf8Bytes(otherTenantEntry),
            new DistributedCacheEntryOptions(),
            CancellationToken.None);

        _business.ListAsync(Arg.Any<CancellationToken>()).Returns(new List<TenantRankServiceModel>());

        var result = await _facade.ListAsync(CancellationToken.None);

        Assert.Empty(result);
        await _business.Received(1).ListAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_Invalid_ThrowsValidationException_AndNeverReachesBusinessOrCache()
    {
        // A mocked cache here, not the shared in-memory one, so "never reaches the cache" is an actual
        // assertion rather than true only because validation happens to run first today.
        var mockCache = Substitute.For<IDistributedCache>();
        var facade = CreateFacade(mockCache);

        // The colour is the interesting one: `#fff; background: url(...)` is the CSS injection the
        // format rule exists to stop, not a cosmetic complaint.
        var viewModel = new CreateRankViewModel
        {
            Name = "",
            Colour = "#fff; background: url(https://example.invalid/leak)",
            SortOrder = 5000,
        };

        var ex = await Assert.ThrowsAsync<ValidationException>(() => facade.CreateAsync(viewModel, CancellationToken.None));

        Assert.Contains(ex.Errors, e => e.PropertyName == nameof(CreateRankViewModel.Name));
        Assert.Contains(ex.Errors, e => e.PropertyName == nameof(CreateRankViewModel.Colour));
        Assert.Contains(ex.Errors, e => e.PropertyName == nameof(CreateRankViewModel.SortOrder));
        await _business.DidNotReceiveWithAnyArgs().CreateAsync(default!, default);
        await mockCache.DidNotReceiveWithAnyArgs().RemoveAsync(default!, default);
    }

    [Fact]
    public async Task Create_Valid_InvalidatesTheCachedList()
    {
        await SeedCachedListAsync();
        _business.CreateAsync(Arg.Any<CreateRankViewModel>(), Arg.Any<CancellationToken>())
            .Returns(new TenantRankServiceModel { Id = Guid.NewGuid(), Name = "Social", SortOrder = 30, Colour = "#3e9c77" });

        await _facade.CreateAsync(
            new CreateRankViewModel { Name = "Social", SortOrder = 30, Colour = "#3e9c77" }, CancellationToken.None);

        Assert.Null(await _cache.GetAsync(TenantKey, CancellationToken.None));
    }

    [Fact]
    public async Task Update_Valid_InvalidatesTheCachedList()
    {
        await SeedCachedListAsync();
        var rankId = Guid.NewGuid();
        _business.UpdateAsync(rankId, Arg.Any<UpdateRankViewModel>(), Arg.Any<CancellationToken>())
            .Returns(new TenantRankServiceModel { Id = rankId, Name = "Raider", SortOrder = 5, Colour = "#cba76a" });

        var result = await _facade.UpdateAsync(
            rankId, new UpdateRankViewModel { Name = "Raider", SortOrder = 5, Colour = "#cba76a" }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Null(await _cache.GetAsync(TenantKey, CancellationToken.None));
    }

    [Fact]
    public async Task Update_Invalid_ThrowsValidationException_AndNeverReachesBusiness()
    {
        var viewModel = new UpdateRankViewModel { Name = "Raider", SortOrder = 10, Colour = "red" };

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => _facade.UpdateAsync(Guid.NewGuid(), viewModel, CancellationToken.None));

        Assert.Contains(ex.Errors, e => e.PropertyName == nameof(UpdateRankViewModel.Colour));
        await _business.DidNotReceiveWithAnyArgs().UpdateAsync(default, default!, default);
    }

    [Fact]
    public async Task Delete_InvalidatesTheCachedList()
    {
        await SeedCachedListAsync();

        await _facade.DeleteAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(await _cache.GetAsync(TenantKey, CancellationToken.None));
    }

    private Task SeedCachedListAsync() =>
        _cache.SetAsync(
            TenantKey,
            JsonSerializer.SerializeToUtf8Bytes(new List<TenantRankServiceModel>
            {
                new() { Id = Guid.NewGuid(), Name = "Raider", SortOrder = 10, Colour = "#cba76a" },
            }),
            new DistributedCacheEntryOptions(),
            CancellationToken.None);
}
