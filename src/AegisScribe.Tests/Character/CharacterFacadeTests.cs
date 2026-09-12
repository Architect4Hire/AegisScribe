using System.Text.Json;
using AegisScribe.Domain.Business;
using AegisScribe.Domain.Facade;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;
using AegisScribe.Domain.Managers.Validators;
using FluentValidation;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace AegisScribe.Tests.Character;

// Facade: real validators, mocked business, a real in-memory IDistributedCache (4.4, add-endpoint
// skill). This is the first facade in the repo that actually reads/writes the cache rather than just
// building the key (2.8) — hit, miss and validation failure are the trio the skill calls out as the
// most-skipped set.
public class CharacterFacadeTests
{
    private readonly ICharacterBusiness _business = Substitute.For<ICharacterBusiness>();
    private readonly IDistributedCache _cache = CreateCache();
    private readonly CharacterFacade _facade;

    public CharacterFacadeTests()
    {
        _facade = new CharacterFacade(
            _business,
            new CharacterLookupViewModelValidator(),
            new SearchCharactersViewModelValidator(),
            _cache);
    }

    private static IDistributedCache CreateCache()
    {
        var services = new ServiceCollection();
        services.AddDistributedMemoryCache();
        return services.BuildServiceProvider().GetRequiredService<IDistributedCache>();
    }

    [Fact]
    public async Task GetCharacter_CacheMiss_CallsBusiness_AndPopulatesTheBareGlobalKey()
    {
        var viewModel = new CharacterLookupViewModel { Region = "us", RealmSlug = "emberfall", Name = "Thrall" };
        var expected = new CharacterDetailServiceModel { Id = Guid.NewGuid(), RealmSlug = "emberfall", Name = "Thrall" };
        _business.GetCharacterAsync("us", "emberfall", "Thrall", Arg.Any<CancellationToken>()).Returns(expected);

        var result = await _facade.GetCharacterAsync(viewModel, CancellationToken.None);

        Assert.Equal(expected.Id, result!.Id);
        await _business.Received(1).GetCharacterAsync("us", "emberfall", "Thrall", Arg.Any<CancellationToken>());

        // Bare key — no tenant prefix (tenancy.md/RESTRICTION) — proven by reading it back directly.
        var cachedBytes = await _cache.GetAsync("char:us:emberfall:thrall", CancellationToken.None);
        Assert.NotNull(cachedBytes);
        Assert.Equal(expected.Id, JsonSerializer.Deserialize<CharacterDetailServiceModel>(cachedBytes!)!.Id);
    }

    [Fact]
    public async Task GetCharacter_CacheHit_NeverCallsBusiness()
    {
        var viewModel = new CharacterLookupViewModel { Region = "us", RealmSlug = "emberfall", Name = "Thrall" };
        var cached = new CharacterDetailServiceModel { Id = Guid.NewGuid(), RealmSlug = "emberfall", Name = "Thrall" };
        await _cache.SetAsync(
            "char:us:emberfall:thrall",
            JsonSerializer.SerializeToUtf8Bytes(cached),
            new DistributedCacheEntryOptions(),
            CancellationToken.None);

        var result = await _facade.GetCharacterAsync(viewModel, CancellationToken.None);

        Assert.Equal(cached.Id, result!.Id);
        await _business.DidNotReceiveWithAnyArgs().GetCharacterAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task GetCharacter_Invalid_ThrowsValidationException_AndNeverReachesBusinessOrCache()
    {
        // A mocked cache here, not the shared in-memory one, so "never reaches the cache" is an
        // actual assertion rather than true only because validation happens to run first today.
        var mockCache = Substitute.For<IDistributedCache>();
        var facade = new CharacterFacade(
            _business, new CharacterLookupViewModelValidator(), new SearchCharactersViewModelValidator(), mockCache);
        var viewModel = new CharacterLookupViewModel { Region = "US1", RealmSlug = "Not A Slug!", Name = "" };

        var ex = await Assert.ThrowsAsync<ValidationException>(() => facade.GetCharacterAsync(viewModel, CancellationToken.None));

        Assert.Contains(ex.Errors, e => e.PropertyName == nameof(CharacterLookupViewModel.Region));
        Assert.Contains(ex.Errors, e => e.PropertyName == nameof(CharacterLookupViewModel.RealmSlug));
        Assert.Contains(ex.Errors, e => e.PropertyName == nameof(CharacterLookupViewModel.Name));
        await _business.DidNotReceiveWithAnyArgs().GetCharacterAsync(default!, default!, default!, default);
        await mockCache.DidNotReceiveWithAnyArgs().GetAsync(default!, default);
        await mockCache.DidNotReceiveWithAnyArgs().SetAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task SearchCharacters_Valid_Delegates()
    {
        var viewModel = new SearchCharactersViewModel { Region = "us", Name = "thra", Limit = 25 };
        var expected = new List<CharacterSummaryServiceModel> { new() { Name = "Thrall" } };
        _business.SearchCharactersAsync("us", null, "thra", null, null, 25, Arg.Any<CancellationToken>()).Returns(expected);

        var result = await _facade.SearchCharactersAsync(viewModel, CancellationToken.None);

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task SearchCharacters_LimitOutOfRange_ThrowsValidationException_AndNeverReachesBusiness()
    {
        var viewModel = new SearchCharactersViewModel { Region = "us", Limit = 500 };

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _facade.SearchCharactersAsync(viewModel, CancellationToken.None));

        Assert.Contains(ex.Errors, e => e.PropertyName == nameof(SearchCharactersViewModel.Limit));
        await _business.DidNotReceiveWithAnyArgs().SearchCharactersAsync(default!, default, default, default, default, default, default);
    }
}
