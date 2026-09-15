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

// Facade: real validator, mocked business, a real in-memory IDistributedCache — the add-endpoint
// skill's hit / miss / validation-failure trio, plus the invalidation this facade owes because it
// writes, plus the key shape that makes caching it safe at all.
public class GuildRankNameFacadeTests
{
    private static readonly Guid TenantId = Guid.Parse("5c9d1e2f-3a4b-4c5d-8e6f-7a8b9c0d1e2f");
    private static readonly string CacheKey = $"t:{TenantId}:guild-rank-names";

    private readonly IGuildRankNameBusiness _business = Substitute.For<IGuildRankNameBusiness>();
    private readonly IDistributedCache _cache = CreateCache();
    private readonly GuildRankNameFacade _facade;

    public GuildRankNameFacadeTests()
    {
        _facade = CreateFacade(_cache);
    }

    private GuildRankNameFacade CreateFacade(IDistributedCache cache)
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(TenantId);

        return new GuildRankNameFacade(
            _business, new SetGuildRankNameViewModelValidator(), cache, tenantContext);
    }

    private static IDistributedCache CreateCache()
    {
        var services = new ServiceCollection();
        services.AddDistributedMemoryCache();
        return services.BuildServiceProvider().GetRequiredService<IDistributedCache>();
    }

    private static List<GuildRankNameServiceModel> Rows(string? name = "Veteran") =>
        [new() { GuildId = Guid.NewGuid(), GuildName = "Emberfall", Rank = 3, Name = name }];

    [Fact]
    public async Task List_CacheMiss_CallsBusiness_AndPopulatesTheTenantPrefixedKey()
    {
        _business.ListAsync(Arg.Any<CancellationToken>()).Returns(Rows());

        var result = await _facade.ListAsync(CancellationToken.None);

        Assert.Equal("Veteran", Assert.Single(result).Name);
        await _business.Received(1).ListAsync(Arg.Any<CancellationToken>());

        // Prefixed with the tenant, and nothing under a bare key. A bare key here would tell one
        // community what another calls the ranks of a guild they both follow.
        Assert.NotNull(await _cache.GetAsync(CacheKey, CancellationToken.None));
        Assert.Null(await _cache.GetAsync("guild-rank-names", CancellationToken.None));
    }

    [Fact]
    public async Task List_CacheHit_NeverCallsBusiness()
    {
        await SeedAsync("Raider");

        var result = await _facade.ListAsync(CancellationToken.None);

        Assert.Equal("Raider", Assert.Single(result).Name);
        await _business.DidNotReceiveWithAnyArgs().ListAsync(default);
    }

    [Fact]
    public async Task List_AnotherTenantsCacheEntry_IsNotServed()
    {
        // The same logical key under a different tenant's prefix. If CacheKey(...) ever lost its
        // prefix, this entry would be returned here.
        await _cache.SetAsync(
            $"t:{Guid.NewGuid()}:guild-rank-names",
            JsonSerializer.SerializeToUtf8Bytes(Rows("SomebodyElsesName")),
            new DistributedCacheEntryOptions(),
            CancellationToken.None);

        _business.ListAsync(Arg.Any<CancellationToken>()).Returns([]);

        Assert.Empty(await _facade.ListAsync(CancellationToken.None));
        await _business.Received(1).ListAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Set_OverlongName_ThrowsValidationException_AndNeverReachesBusinessOrCache()
    {
        // A mocked cache, not the shared in-memory one, so "never reaches the cache" is an actual
        // assertion rather than true only because validation happens to run first today.
        var mockCache = Substitute.For<IDistributedCache>();
        var facade = CreateFacade(mockCache);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => facade.SetAsync(
            Guid.NewGuid(), 3, new SetGuildRankNameViewModel { Name = new string('x', 100) }, CancellationToken.None));

        Assert.Contains(ex.Errors, e => e.PropertyName == nameof(SetGuildRankNameViewModel.Name));
        await _business.DidNotReceiveWithAnyArgs().SetAsync(default, default, default!, default);
        await mockCache.DidNotReceiveWithAnyArgs().RemoveAsync(default!, default);
    }

    [Fact]
    public async Task Set_InvalidatesTheCachedList()
    {
        await SeedAsync("Stale");
        _business.SetAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<SetGuildRankNameViewModel>(), Arg.Any<CancellationToken>())
            .Returns(true);

        await _facade.SetAsync(
            Guid.NewGuid(), 3, new SetGuildRankNameViewModel { Name = "Veteran" }, CancellationToken.None);

        Assert.Null(await _cache.GetAsync(CacheKey, CancellationToken.None));
    }

    [Fact]
    public async Task Set_ForAnUnfollowedGuild_StillInvalidates()
    {
        // Invalidated even when nothing was written: one unnecessary Redis delete costs nothing next
        // to a branch that could get the "we changed something" case wrong later.
        await SeedAsync("Stale");
        _business.SetAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<SetGuildRankNameViewModel>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var set = await _facade.SetAsync(
            Guid.NewGuid(), 3, new SetGuildRankNameViewModel { Name = "Veteran" }, CancellationToken.None);

        Assert.False(set);
        Assert.Null(await _cache.GetAsync(CacheKey, CancellationToken.None));
    }

    private Task SeedAsync(string name) =>
        _cache.SetAsync(
            CacheKey,
            JsonSerializer.SerializeToUtf8Bytes(Rows(name)),
            new DistributedCacheEntryOptions(),
            CancellationToken.None);
}
