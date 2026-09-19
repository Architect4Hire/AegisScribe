using AegisScribe.Domain.Data;
using AegisScribe.Domain.Integration.Blizzard;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.SyncWorker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace AegisScribe.Tests.Integration.Blizzard;

// The media backfill pass. The selection and the set-based writes run against real SQL in
// CharacterMediaRepositoryTests; what is pinned here is the pass itself — one call per item id, a 404
// recorded rather than retried forever, an outage retried rather than recorded, and no work at all
// without credentials.
public class MediaBackfillSyncTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-19T12:00:00Z");

    private readonly IBlizzardGateway _gateway = Substitute.For<IBlizzardGateway>();
    private readonly ICharacterRepository _repository = Substitute.For<ICharacterRepository>();

    public MediaBackfillSyncTests()
    {
        _gateway.IsConfigured.Returns(true);

        _repository.FindMissingMediaAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<StaleCharacterRef>)[]);
        _repository.FindItemIdsNeedingIconAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<long>)[]);
    }

    [Fact]
    public async Task Run_AsksOncePerItemId_AndWritesTheAnswerForAllRowsWearingIt()
    {
        ItemsNeedIcons(19019, 71086);
        _gateway.FetchItemIconAsync(19019, Arg.Any<CancellationToken>()).Returns(new ItemIconLookup("135349"));
        _gateway.FetchItemIconAsync(71086, Arg.Any<CancellationToken>()).Returns(new ItemIconLookup("458973"));

        var result = await CreateSync().RunAsync(CancellationToken.None);

        Assert.Equal(2, result.IconsResolved);
        await _gateway.Received(1).FetchItemIconAsync(19019, Arg.Any<CancellationToken>());
        await _gateway.Received(1).FetchItemIconAsync(71086, Arg.Any<CancellationToken>());
        await _repository.Received(1).SetItemIconAsync(19019, "135349", Now, Arg.Any<CancellationToken>());
        await _repository.Received(1).SetItemIconAsync(71086, "458973", Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_RecordsA404AsNoIcon_SoTheItemLeavesTheQueue()
    {
        ItemsNeedIcons(1);
        _gateway.FetchItemIconAsync(1, Arg.Any<CancellationToken>()).Returns(ItemIconLookup.NotFound);

        await CreateSync().RunAsync(CancellationToken.None);

        await _repository.Received(1).SetItemIconAsync(1, null, Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_LeavesAnItemQueuedWhenBlizzardIsUnavailable()
    {
        // An outage is "could not ask", not "no icon". Recording it would blank the icon until the
        // staleness window came round again.
        ItemsNeedIcons(19019);
        _gateway.FetchItemIconAsync(19019, Arg.Any<CancellationToken>())
            .Returns<ItemIconLookup>(_ => throw new BlizzardUnavailableException("down"));

        var result = await CreateSync().RunAsync(CancellationToken.None);

        Assert.Equal(1, result.Failed);
        await _repository.DidNotReceiveWithAnyArgs().SetItemIconAsync(default, default, default, default);
    }

    [Fact]
    public async Task Run_FillsRendersForCharactersThatHaveNone()
    {
        var characterId = Guid.NewGuid();
        _repository.FindMissingMediaAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<StaleCharacterRef>)[new StaleCharacterRef(characterId, Guid.NewGuid(), "us", "argent-dawn", "Aldric")]);

        var media = new CharacterMedia("https://render/avatar.jpg", "https://render/main-raw.png");
        _gateway.FetchCharacterMediaAsync("argent-dawn", "Aldric", Arg.Any<CancellationToken>()).Returns(media);

        var result = await CreateSync().RunAsync(CancellationToken.None);

        Assert.Equal(1, result.RendersResolved);
        await _repository.Received(1).SetMediaAsync(characterId, media, Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_DoesNothingWithoutCredentials()
    {
        _gateway.IsConfigured.Returns(false);
        ItemsNeedIcons(19019);

        var result = await CreateSync().RunAsync(CancellationToken.None);

        Assert.False(result.Ran);
        await _gateway.DidNotReceiveWithAnyArgs().FetchItemIconAsync(default, default);
        await _repository.DidNotReceiveWithAnyArgs().FindItemIdsNeedingIconAsync(default, default, default);
    }

    [Fact]
    public async Task Run_AsksForNoMoreThanTheBatchSize()
    {
        await CreateSync(batchSize: 25).RunAsync(CancellationToken.None);

        await _repository.Received(1).FindMissingMediaAsync(Arg.Any<DateTimeOffset>(), 25, Arg.Any<CancellationToken>());
        await _repository.Received(1).FindItemIdsNeedingIconAsync(Arg.Any<DateTimeOffset>(), 25, Arg.Any<CancellationToken>());
    }

    private void ItemsNeedIcons(params long[] itemIds) =>
        _repository.FindItemIdsNeedingIconAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<long>)itemIds);

    private MediaBackfillSync CreateSync(int batchSize = 100)
    {
        // A real scope factory, because the pass opens a scope per unit of work — the shape it runs in.
        var services = new ServiceCollection();
        services.AddSingleton(_gateway);
        services.AddSingleton(_repository);

        var time = new FakeTimeProvider(Now);

        return new MediaBackfillSync(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new BlizzardStalenessPolicy(
                new OptionsWrapper<BlizzardStalenessOptions>(new BlizzardStalenessOptions()),
                time),
            new OptionsWrapper<SyncWorkerOptions>(
                new SyncWorkerOptions { MediaBatchSize = batchSize, MaxConcurrentRefreshes = 4 }),
            time,
            NullLogger<MediaBackfillSync>.Instance);
    }
}
