using AegisScribe.Domain.Data;
using AegisScribe.Domain.Integration.Blizzard;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AegisScribe.Tests.Character;

// The cache-first read: fresh, stale, gateway-failure-falls-back, and missing. The repository itself is
// exercised against real SQL by CharacterRepositoryTests; everything here is about the sequencing.
public class CharacterDataLayerTests
{
    private const string Region = "us";
    private const string RealmSlug = "emberfall";
    private const string Name = "Thrall";

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-14T12:00:00Z");

    private readonly ICharacterRepository _repository = Substitute.For<ICharacterRepository>();
    private readonly IRealmRepository _realms = Substitute.For<IRealmRepository>();
    private readonly IBlizzardGateway _gateway = Substitute.For<IBlizzardGateway>();
    private readonly IBlizzardStalenessPolicy _staleness = Substitute.For<IBlizzardStalenessPolicy>();
    private readonly CharacterDataLayer _dataLayer;

    public CharacterDataLayerTests()
    {
        _gateway.IsConfigured.Returns(true);

        // Nothing is stale unless a test says so, so a test that cares about staleness has to state it.
        _staleness.IsStale(Arg.Any<DateTimeOffset>()).Returns(false);

        // Mirrors the real repository on insert: the gateway leaves Id unset, and the upsert assigns
        // one. Without this the substitute hands back null and the equipment write has nothing to
        // attach to.
        _repository
            .UpsertCharacterAsync(Arg.Any<Domain.Managers.Models.Domain.Character>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var fresh = call.Arg<Domain.Managers.Models.Domain.Character>();
                if (fresh.Id == Guid.Empty)
                {
                    fresh.Id = Guid.NewGuid();
                }

                return fresh;
            });

        // The default execution strategy stand-in: run the unit once, like a transaction that did not
        // need retrying. The retry test below replaces it deliberately.
        _repository
            .ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task<Domain.Managers.Models.Domain.Character>>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task<Domain.Managers.Models.Domain.Character>>>()(CancellationToken.None));

        _dataLayer = new CharacterDataLayer(
            _repository,
            _realms,
            _gateway,
            _staleness,
            NullLogger<CharacterDataLayer>.Instance);
    }

    [Fact]
    public async Task GetCharacter_WhenTheStoredRowIsFresh_ReturnsItWithoutCallingBlizzard()
    {
        // The case that has to be cheap. Every request that touched Blizzard would burn the 36,000/hour
        // contractual budget on data already sitting in SQL.
        var stored = StoredCharacter();
        StoredIs(stored);

        var result = await _dataLayer.GetCharacterAsync(Region, RealmSlug, Name, CancellationToken.None);

        Assert.Same(stored, result.Character);
        Assert.False(result.IsDegraded);
        await _gateway.DidNotReceiveWithAnyArgs().FetchCharacterAsync(default!, default!, default);
        await _gateway.DidNotReceiveWithAnyArgs().FetchEquipmentAsync(default!, default!, default);
    }

    [Fact]
    public async Task GetCharacter_WhenTheStoredRowIsStale_RefreshesItFromBlizzardAndPersists()
    {
        var stored = StoredCharacter();
        var refreshed = StoredCharacter();
        StoredIs(stored, thenAfterRefresh: refreshed);

        _staleness.IsStale(stored.LastSyncedAt).Returns(true);
        RealmIsKnown();
        BlizzardHas(FetchedCharacter(), FetchedEquipment());

        var result = await _dataLayer.GetCharacterAsync(Region, RealmSlug, Name, CancellationToken.None);

        Assert.Same(refreshed, result.Character);
        Assert.False(result.IsDegraded);
        await _repository.ReceivedWithAnyArgs(1).UpsertCharacterAsync(default!, default, default);
        await _repository.ReceivedWithAnyArgs(1).ReplaceEquipmentAsync(default, default!, default);
    }

    [Fact]
    public async Task GetCharacter_WhenAFreshRowHasNeverHadRenders_FetchesOnlyTheRenders()
    {
        // Somebody is looking at it now. One call for the renders, not three for data that is current.
        var stored = StoredCharacter();
        stored.MediaSyncedAt = null;
        StoredIs(stored);
        var media = new CharacterMedia("https://render/avatar.jpg", "https://render/main-raw.png");
        _gateway.FetchCharacterMediaAsync(RealmSlug, Name, Arg.Any<CancellationToken>()).Returns(media);

        var result = await _dataLayer.GetCharacterAsync(Region, RealmSlug, Name, CancellationToken.None);

        Assert.Equal("https://render/main-raw.png", result.Character!.RenderUrl);
        await _repository.Received(1).SetMediaAsync(stored.Id, media, stored.LastSyncedAt, Arg.Any<CancellationToken>());
        await _gateway.DidNotReceiveWithAnyArgs().FetchCharacterAsync(default!, default!, default);
        await _gateway.DidNotReceiveWithAnyArgs().FetchEquipmentAsync(default!, default!, default);
    }

    [Fact]
    public async Task GetCharacter_OnARefresh_ResolvesIconsForItemsNobodyHasSeen_OncePerItem()
    {
        // A guild member opened for the first time: this request fetched their gear, and the page that
        // asked should get its icons now rather than after the worker's next pass.
        var stored = StoredCharacter();
        StoredIs(stored, thenAfterRefresh: StoredCharacter());
        _staleness.IsStale(stored.LastSyncedAt).Returns(true);
        RealmIsKnown();
        BlizzardHas(FetchedCharacter(), Equipped((EquipmentSlot.Finger1, 19019), (EquipmentSlot.Finger2, 19019), (EquipmentSlot.Head, 71086)));
        _gateway.FetchItemIconAsync(19019, Arg.Any<CancellationToken>()).Returns(new ItemIconLookup("135349"));
        _gateway.FetchItemIconAsync(71086, Arg.Any<CancellationToken>()).Returns(ItemIconLookup.NotFound);

        await _dataLayer.GetCharacterAsync(Region, RealmSlug, Name, CancellationToken.None);

        // Two rings, one item: one call.
        await _gateway.Received(1).FetchItemIconAsync(19019, Arg.Any<CancellationToken>());
        await _repository.Received(1).SetItemIconAsync(19019, "135349", Now, Arg.Any<CancellationToken>());
        // A 404 is recorded too, so nobody asks again.
        await _repository.Received(1).SetItemIconAsync(71086, null, Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetCharacter_DoesNotAskAboutAnItemWhoseIconIsAlreadyKnown()
    {
        var stored = StoredCharacter();
        StoredIs(stored, thenAfterRefresh: StoredCharacter());
        _staleness.IsStale(stored.LastSyncedAt).Returns(true);
        RealmIsKnown();

        // The equipment write copies known icons onto the incoming items; this stands in for that.
        var equipment = Equipped((EquipmentSlot.Head, 19019));
        equipment.EquippedItems.Single().IconName = "135349";
        equipment.EquippedItems.Single().IconSyncedAt = Now;
        BlizzardHas(FetchedCharacter(), equipment);

        await _dataLayer.GetCharacterAsync(Region, RealmSlug, Name, CancellationToken.None);

        await _gateway.DidNotReceiveWithAnyArgs().FetchItemIconAsync(default, default);
    }

    [Fact]
    public async Task GetCharacter_OnAFreshRow_ResolvesUnknownIconsAndReturnsThem()
    {
        // Gear fetched before icons existed, or while the icon call was down: current, but outlined.
        var stored = StoredCharacter();
        stored.Equipment = Equipped((EquipmentSlot.Head, 19019));
        StoredIs(stored);
        _gateway.FetchItemIconAsync(19019, Arg.Any<CancellationToken>()).Returns(new ItemIconLookup("135349"));

        var result = await _dataLayer.GetCharacterAsync(Region, RealmSlug, Name, CancellationToken.None);

        Assert.Equal("135349", result.Character!.Equipment!.EquippedItems.Single().IconName);
        await _repository.Received(1).SetItemIconAsync(19019, "135349", stored.LastSyncedAt, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetCharacter_LeavesAnIconForTheWorkerWhenBlizzardIsUnavailable()
    {
        var stored = StoredCharacter();
        stored.Equipment = Equipped((EquipmentSlot.Head, 19019));
        StoredIs(stored);
        _gateway.FetchItemIconAsync(19019, Arg.Any<CancellationToken>())
            .Returns<ItemIconLookup>(_ => throw new BlizzardUnavailableException("down"));

        var result = await _dataLayer.GetCharacterAsync(Region, RealmSlug, Name, CancellationToken.None);

        Assert.False(result.IsDegraded);
        await _repository.DidNotReceiveWithAnyArgs().SetItemIconAsync(default, default, default, default);
    }

    [Fact]
    public async Task GetCharacter_WhenAFreshRowAlreadyHasRenders_DoesNotAskAgain()
    {
        StoredIs(StoredCharacter());

        await _dataLayer.GetCharacterAsync(Region, RealmSlug, Name, CancellationToken.None);

        await _gateway.DidNotReceiveWithAnyArgs().FetchCharacterMediaAsync(default!, default!, default);
    }

    [Fact]
    public async Task GetCharacter_WhenTheRendersAreUnavailable_ServesTheFreshRowWithoutThem()
    {
        var stored = StoredCharacter();
        stored.MediaSyncedAt = null;
        StoredIs(stored);
        _gateway.FetchCharacterMediaAsync(RealmSlug, Name, Arg.Any<CancellationToken>())
            .Returns<CharacterMedia?>(_ => throw new BlizzardUnavailableException("down"));

        var result = await _dataLayer.GetCharacterAsync(Region, RealmSlug, Name, CancellationToken.None);

        Assert.Same(stored, result.Character);
        Assert.False(result.IsDegraded);
        await _repository.DidNotReceiveWithAnyArgs().SetMediaAsync(default, default!, default, default);
    }

    [Fact]
    public async Task GetCharacter_OnARefresh_FetchesTheRendersAndPersistsThemWithTheCharacter()
    {
        var stored = StoredCharacter();
        StoredIs(stored, thenAfterRefresh: StoredCharacter());
        _staleness.IsStale(stored.LastSyncedAt).Returns(true);
        RealmIsKnown();
        BlizzardHas(FetchedCharacter(), FetchedEquipment());
        _gateway.FetchCharacterMediaAsync(RealmSlug, Name, Arg.Any<CancellationToken>())
            .Returns(new CharacterMedia("https://render/avatar.jpg", "https://render/main-raw.png"));

        await _dataLayer.GetCharacterAsync(Region, RealmSlug, Name, CancellationToken.None);

        // Stamped with the summary's fetch instant, so the renders age with the character.
        await _repository.Received(1).UpsertCharacterAsync(
            Arg.Is<Domain.Managers.Models.Domain.Character>(c =>
                c.RenderUrl == "https://render/main-raw.png" && c.MediaSyncedAt == Now),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetCharacter_WhenOnlyTheRendersAreUnavailable_StillRefreshesAndIsNotDegraded()
    {
        // Decoration failing must not turn fresh data into a degraded read.
        var stored = StoredCharacter();
        var refreshed = StoredCharacter();
        StoredIs(stored, thenAfterRefresh: refreshed);
        _staleness.IsStale(stored.LastSyncedAt).Returns(true);
        RealmIsKnown();
        BlizzardHas(FetchedCharacter(), FetchedEquipment());
        _gateway.FetchCharacterMediaAsync(RealmSlug, Name, Arg.Any<CancellationToken>())
            .Returns<CharacterMedia?>(_ => throw new BlizzardUnavailableException("down"));

        var result = await _dataLayer.GetCharacterAsync(Region, RealmSlug, Name, CancellationToken.None);

        Assert.Same(refreshed, result.Character);
        Assert.False(result.IsDegraded);
        await _repository.Received(1).UpsertCharacterAsync(
            Arg.Is<Domain.Managers.Models.Domain.Character>(c => c.MediaSyncedAt == null),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetCharacter_WhenTheEquipmentSnapshotIsStale_RefreshesEvenThoughTheCharacterRowIsNot()
    {
        // Gear and the summary come from separate endpoints with separate LastSyncedAt columns, but a
        // read refreshes them together — so a character whose gear has aged out is not current, whatever
        // its own column says.
        var stored = StoredCharacter();
        stored.Equipment!.LastSyncedAt = Now.AddDays(-40);
        StoredIs(stored, thenAfterRefresh: StoredCharacter());

        _staleness.IsStale(stored.LastSyncedAt).Returns(false);
        _staleness.IsStale(stored.Equipment.LastSyncedAt).Returns(true);
        RealmIsKnown();
        BlizzardHas(FetchedCharacter(), FetchedEquipment());

        await _dataLayer.GetCharacterAsync(Region, RealmSlug, Name, CancellationToken.None);

        await _gateway.Received(1).FetchCharacterAsync(RealmSlug, Name, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetCharacter_WhenTheStoredRowHasNoEquipmentAtAll_TreatsItAsStale()
    {
        var stored = StoredCharacter();
        stored.Equipment = null;
        StoredIs(stored, thenAfterRefresh: StoredCharacter());

        RealmIsKnown();
        BlizzardHas(FetchedCharacter(), FetchedEquipment());

        await _dataLayer.GetCharacterAsync(Region, RealmSlug, Name, CancellationToken.None);

        await _gateway.Received(1).FetchCharacterAsync(RealmSlug, Name, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetCharacter_WhenBlizzardIsUnavailable_FallsBackToTheStaleRowAndFlagsItDegraded()
    {
        // The whole point of the fallback: a Blizzard outage degrades the page, it does not empty it.
        var stored = StoredCharacter();
        StoredIs(stored);

        _staleness.IsStale(stored.LastSyncedAt).Returns(true);
        RealmIsKnown();
        _gateway.FetchCharacterAsync(RealmSlug, Name, Arg.Any<CancellationToken>())
            .Returns<Task<Domain.Managers.Models.Domain.Character?>>(_ => throw new BlizzardUnavailableException("Blizzard could not be reached."));

        var result = await _dataLayer.GetCharacterAsync(Region, RealmSlug, Name, CancellationToken.None);

        Assert.Same(stored, result.Character);
        Assert.True(result.IsDegraded);
        await _repository.DidNotReceiveWithAnyArgs().UpsertCharacterAsync(default!, default, default);
    }

    [Fact]
    public async Task GetCharacter_WhenBlizzardNoLongerKnowsTheCharacter_KeepsTheStoredRowRatherThanDeletingIt()
    {
        // A 404 on a character we hold means renamed, transferred or deleted. Removing other people's
        // data on the strength of a 404 is the erasure routine's job, not a read path's.
        var stored = StoredCharacter();
        StoredIs(stored);

        _staleness.IsStale(stored.LastSyncedAt).Returns(true);
        RealmIsKnown();
        BlizzardHas(character: null, equipment: null);

        var result = await _dataLayer.GetCharacterAsync(Region, RealmSlug, Name, CancellationToken.None);

        Assert.Same(stored, result.Character);
        Assert.True(result.IsDegraded);
    }

    [Fact]
    public async Task GetCharacter_WhenNothingIsStoredAndBlizzardHasIt_FetchesPersistsAndReturnsIt()
    {
        // The never-synced case: an empty store, a character arriving from Blizzard, and a row to show
        // for it afterwards.
        var persisted = StoredCharacter();
        StoredIs(null, thenAfterRefresh: persisted);

        RealmIsKnown();
        BlizzardHas(FetchedCharacter(), FetchedEquipment());

        var result = await _dataLayer.GetCharacterAsync(Region, RealmSlug, Name, CancellationToken.None);

        Assert.Same(persisted, result.Character);
        Assert.False(result.IsDegraded);
        await _repository.ReceivedWithAnyArgs(1).UpsertCharacterAsync(default!, default, default);
    }

    [Fact]
    public async Task GetCharacter_WhenNothingIsStoredAndBlizzardHasNothing_ReturnsNotFound()
    {
        StoredIs(null);
        RealmIsKnown();
        BlizzardHas(character: null, equipment: null);

        var result = await _dataLayer.GetCharacterAsync(Region, RealmSlug, Name, CancellationToken.None);

        Assert.Null(result.Character);

        // Not degraded: there is nothing stale being served, there is simply no such character.
        Assert.False(result.IsDegraded);
    }

    [Fact]
    public async Task GetCharacter_OnAnUnknownRealm_FetchesTheRealmFirstAndPersistsItWithTheCharacter()
    {
        // Character.RealmId is not nullable, so a realm this app has never stored has to be resolved
        // before anything can be hung from it.
        var persisted = StoredCharacter();
        StoredIs(null, thenAfterRefresh: persisted);

        var fetchedRealm = new Realm { Slug = RealmSlug, Name = "Emberfall", Region = Region, BlizzardConnectedRealmId = 1092 };
        var savedRealm = new Realm { Id = Guid.NewGuid(), Slug = RealmSlug, Name = "Emberfall", Region = Region };

        _realms.FindAsync(Region, RealmSlug, Arg.Any<CancellationToken>()).Returns((Realm?)null);
        _gateway.FetchRealmAsync(Region, RealmSlug, Arg.Any<CancellationToken>()).Returns(fetchedRealm);
        _realms.UpsertAsync(fetchedRealm, Arg.Any<CancellationToken>()).Returns(savedRealm);
        BlizzardHas(FetchedCharacter(), FetchedEquipment());

        var result = await _dataLayer.GetCharacterAsync(Region, RealmSlug, Name, CancellationToken.None);

        Assert.Same(persisted, result.Character);
        await _realms.Received(1).UpsertAsync(fetchedRealm, Arg.Any<CancellationToken>());

        // The character is stored against the realm row we just wrote, not against the empty RealmId the
        // gateway left on the entity.
        await _repository.Received(1).UpsertCharacterAsync(Arg.Any<Domain.Managers.Models.Domain.Character>(), savedRealm.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetCharacter_WhenNeitherWeNorBlizzardKnowTheRealm_DoesNotSpendACallOnTheCharacter()
    {
        // There is nothing to hang a character from, so asking for one would spend contractual budget on
        // a lookup that could not be persisted either way.
        StoredIs(null);
        _realms.FindAsync(Region, RealmSlug, Arg.Any<CancellationToken>()).Returns((Realm?)null);
        _gateway.FetchRealmAsync(Region, RealmSlug, Arg.Any<CancellationToken>()).Returns((Realm?)null);

        var result = await _dataLayer.GetCharacterAsync(Region, RealmSlug, Name, CancellationToken.None);

        Assert.Null(result.Character);
        await _gateway.DidNotReceiveWithAnyArgs().FetchCharacterAsync(default!, default!, default);
    }

    [Fact]
    public async Task GetCharacter_WhenTheRealmIsAlreadyStored_DoesNotFetchIt()
    {
        StoredIs(null, thenAfterRefresh: StoredCharacter());
        RealmIsKnown();
        BlizzardHas(FetchedCharacter(), FetchedEquipment());

        await _dataLayer.GetCharacterAsync(Region, RealmSlug, Name, CancellationToken.None);

        await _gateway.DidNotReceiveWithAnyArgs().FetchRealmAsync(default!, default!, default);
    }

    [Fact]
    public async Task GetCharacter_WithNoBlizzardCredentials_ServesStoredDataAndNeverTouchesTheGateway()
    {
        // Offline development is a first-class case (external.md). An unconfigured deployment must not
        // pay for a token mint and a request per read to rediscover that it has no credentials.
        var stored = StoredCharacter();
        StoredIs(stored);

        _staleness.IsStale(stored.LastSyncedAt).Returns(true);
        _gateway.IsConfigured.Returns(false);

        var result = await _dataLayer.GetCharacterAsync(Region, RealmSlug, Name, CancellationToken.None);

        Assert.Same(stored, result.Character);
        Assert.True(result.IsDegraded);
        await _gateway.DidNotReceiveWithAnyArgs().FetchRealmAsync(default!, default!, default);
        await _gateway.DidNotReceiveWithAnyArgs().FetchCharacterAsync(default!, default!, default);
    }

    [Fact]
    public async Task GetCharacter_WhenTheTransactionUnitIsRetried_DoesNotCallBlizzardAgain()
    {
        // ExecuteInTransactionAsync's callback MAY RUN MORE THAN ONCE, so an HTTP call inside it would
        // fire again on every retry — spending contractual budget, rolled back by nothing. The
        // substitute runs the unit twice, exactly as a retrying execution strategy would.
        StoredIs(null, thenAfterRefresh: StoredCharacter());
        RealmIsKnown();
        BlizzardHas(FetchedCharacter(), FetchedEquipment());

        _repository
            .ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task<Domain.Managers.Models.Domain.Character>>>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var unit = call.Arg<Func<CancellationToken, Task<Domain.Managers.Models.Domain.Character>>>();
                await unit(CancellationToken.None);
                return await unit(CancellationToken.None);
            });

        await _dataLayer.GetCharacterAsync(Region, RealmSlug, Name, CancellationToken.None);

        await _gateway.Received(1).FetchCharacterAsync(RealmSlug, Name, Arg.Any<CancellationToken>());
        await _gateway.Received(1).FetchEquipmentAsync(RealmSlug, Name, Arg.Any<CancellationToken>());

        // The writes, by contrast, are expected to repeat — that is what makes them safe to retry.
        await _repository.ReceivedWithAnyArgs(2).UpsertCharacterAsync(default!, default, default);
    }

    [Fact]
    public async Task Search_Delegates()
    {
        var afterId = Guid.NewGuid();
        var summaries = new List<CharacterSummaryServiceModel> { new() };
        _repository.SearchAsync("us", "emberfall", "thra", "thrall", afterId, 25, Arg.Any<CancellationToken>())
            .Returns(summaries);

        var result = await _dataLayer.SearchAsync("us", "emberfall", "thra", "thrall", afterId, 25, CancellationToken.None);

        Assert.Same(summaries, result);
    }

    [Fact]
    public async Task ExecuteInTransaction_Delegates()
    {
        _repository.ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task<int>>>(), Arg.Any<CancellationToken>())
            .Returns(42);

        var result = await _dataLayer.ExecuteInTransactionAsync(_ => Task.FromResult(1), CancellationToken.None);

        Assert.Equal(42, result);
    }

    // The stored read is called twice on a refresh — once before the fetch and once after the write —
    // so the two answers are scripted separately.
    private void StoredIs(
        Domain.Managers.Models.Domain.Character? stored,
        Domain.Managers.Models.Domain.Character? thenAfterRefresh = null)
    {
        if (thenAfterRefresh is null)
        {
            _repository.FindByRealmAndNameAsync(Region, RealmSlug, Name, Arg.Any<CancellationToken>()).Returns(stored);
            return;
        }

        _repository.FindByRealmAndNameAsync(Region, RealmSlug, Name, Arg.Any<CancellationToken>())
            .Returns(stored, thenAfterRefresh);
    }

    private void RealmIsKnown() =>
        _realms.FindAsync(Region, RealmSlug, Arg.Any<CancellationToken>())
            .Returns(new Realm { Id = Guid.NewGuid(), Slug = RealmSlug, Name = "Emberfall", Region = Region });

    private void BlizzardHas(Domain.Managers.Models.Domain.Character? character, CharacterEquipment? equipment)
    {
        _gateway.FetchCharacterAsync(RealmSlug, Name, Arg.Any<CancellationToken>()).Returns(character);
        _gateway.FetchEquipmentAsync(RealmSlug, Name, Arg.Any<CancellationToken>()).Returns(equipment);
    }

    // Renders already asked for, so a fresh stored row really is "nothing to do" — the never-asked case
    // has tests of its own below.
    private static Domain.Managers.Models.Domain.Character StoredCharacter() => new()
    {
        Id = Guid.NewGuid(),
        Name = Name,
        NameLower = Name.ToLowerInvariant(),
        LastSyncedAt = Now.AddDays(-1),
        MediaSyncedAt = Now.AddDays(-1),
        Equipment = new CharacterEquipment { LastSyncedAt = Now.AddDays(-1) },
    };

    private static Domain.Managers.Models.Domain.Character FetchedCharacter() => new()
    {
        Name = Name,
        NameLower = Name.ToLowerInvariant(),
        BlizzardCharacterId = 176946929,
        LastSyncedAt = Now,
    };

    private static CharacterEquipment FetchedEquipment() => new() { LastSyncedAt = Now };

    // Straight from the equipment endpoint: no icons on anything.
    private static CharacterEquipment Equipped(params (EquipmentSlot Slot, long ItemId)[] items) => new()
    {
        LastSyncedAt = Now,
        EquippedItems = [.. items.Select(item => new EquippedItem
        {
            Slot = item.Slot,
            BlizzardItemId = item.ItemId,
            ItemName = "Item",
            Quality = ItemQuality.Rare,
            ItemLevel = 600,
        })],
    };
}
