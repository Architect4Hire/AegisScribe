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

// 6.5 — the eager refresh pass. What is pinned here is the pass's own behaviour: the budget, the
// concurrency bound, idempotency, and that one character failing does not cost the rest of the batch.
// The selection query is exercised against real SQL by StaleCharacterQueryTests.
public class CharacterRefreshSyncTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-14T12:00:00Z");

    private readonly IBlizzardGateway _gateway = Substitute.For<IBlizzardGateway>();
    private readonly ICharacterRepository _repository = Substitute.For<ICharacterRepository>();

    // A stand-in store: character id to its LastSyncedAt. The selection substitute runs the real
    // predicate against it and the upsert substitute advances it, which is what makes the idempotency
    // test meaningful rather than circular — the second pass asks the same question of state the first
    // pass actually changed.
    private readonly Dictionary<Guid, DateTimeOffset> _store = [];

    public CharacterRefreshSyncTests()
    {
        _gateway.IsConfigured.Returns(true);

        _repository.FindStaleAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var staleBefore = call.ArgAt<DateTimeOffset>(0);
                var take = call.ArgAt<int>(1);

                lock (_store)
                {
                    return (IReadOnlyList<StaleCharacterRef>)
                    [
                        .. _store
                            .Where(row => row.Value < staleBefore)
                            .OrderBy(row => row.Value)
                            .ThenBy(row => row.Key)
                            .Take(take)
                            // The character name IS the id here, so the gateway stub below can tell which
                            // candidate it is being asked about and the upsert can advance the right row.
                            .Select(row => new StaleCharacterRef(row.Key, Guid.NewGuid(), "us", "emberfall", Key(row.Key))),
                    ];
                }
            });

        _repository
            .ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task<Domain.Managers.Models.Domain.Character>>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task<Domain.Managers.Models.Domain.Character>>>()(CancellationToken.None));

        _repository
            .UpsertCharacterAsync(Arg.Any<Domain.Managers.Models.Domain.Character>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var fresh = call.Arg<Domain.Managers.Models.Domain.Character>();
                fresh.Id = fresh.Id == Guid.Empty ? Guid.NewGuid() : fresh.Id;

                // The write is what advances the compliance clock, so only a character that got all the
                // way through the transaction stops being due.
                lock (_store)
                {
                    _store[Guid.ParseExact(fresh.Name, "N")] = fresh.LastSyncedAt;
                }

                return fresh;
            });
    }

    [Fact]
    public async Task Run_RefreshesEveryDueCharacterAndFetchesBothDocumentsForEach()
    {
        StoreHas(count: 3, lastSyncedAt: Now.AddDays(-20));
        BlizzardAnswers();

        var result = await CreateSync().RunAsync(CancellationToken.None);

        Assert.True(result.Ran);
        Assert.Equal(3, result.Selected);
        Assert.Equal(3, result.Refreshed);

        // Three calls per character — the summary, the equipment and the renders — which is what the
        // per-run budget is sized against.
        await _gateway.ReceivedWithAnyArgs(3).FetchCharacterAsync(default!, default!, default);
        await _gateway.ReceivedWithAnyArgs(3).FetchEquipmentAsync(default!, default!, default);
        await _gateway.ReceivedWithAnyArgs(3).FetchCharacterMediaAsync(default!, default!, default);
    }

    [Fact]
    public async Task Run_CarriesTheRendersOntoTheCharacterItUpserts()
    {
        StoreHas(count: 1, lastSyncedAt: Now.AddDays(-20));
        BlizzardAnswers();
        _gateway.FetchCharacterMediaAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CharacterMedia("https://render/avatar.jpg", "https://render/main-raw.png"));

        await CreateSync().RunAsync(CancellationToken.None);

        await _repository.Received(1).UpsertCharacterAsync(
            Arg.Is<Domain.Managers.Models.Domain.Character>(c =>
                c.AvatarUrl == "https://render/avatar.jpg" && c.MediaSyncedAt == Now),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_StillRefreshesTheCharacterWhenOnlyTheRendersAreUnavailable()
    {
        // The renders are decoration; the summary and gear are the data. A media failure must not cost
        // the refresh, and must leave MediaSyncedAt unset so the stored renders survive the upsert.
        StoreHas(count: 1, lastSyncedAt: Now.AddDays(-20));
        BlizzardAnswers();
        _gateway.FetchCharacterMediaAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<CharacterMedia?>(_ => throw new BlizzardUnavailableException("down"));

        var result = await CreateSync().RunAsync(CancellationToken.None);

        Assert.Equal(1, result.Refreshed);
        await _repository.Received(1).UpsertCharacterAsync(
            Arg.Is<Domain.Managers.Models.Domain.Character>(c => c.MediaSyncedAt == null),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_Twice_DoesNothingTheSecondTime()
    {
        // The idempotency the prompt asks for. The first pass advances LastSyncedAt, so the second runs
        // the same predicate and finds nothing due — zero Blizzard calls, not merely "no duplicate
        // rows". A repeated or interrupted run is therefore free rather than expensive.
        StoreHas(count: 5, lastSyncedAt: Now.AddDays(-20));
        BlizzardAnswers();

        var sync = CreateSync();

        Assert.Equal(5, (await sync.RunAsync(CancellationToken.None)).Refreshed);

        _gateway.ClearReceivedCalls();

        var second = await sync.RunAsync(CancellationToken.None);

        Assert.False(second.Ran);
        Assert.Equal(0, second.Selected);
        await _gateway.DidNotReceiveWithAnyArgs().FetchCharacterAsync(default!, default!, default);
    }

    [Fact]
    public async Task Run_NeverSelectsMoreThanTheConfiguredBudget()
    {
        StoreHas(count: 50, lastSyncedAt: Now.AddDays(-20));
        BlizzardAnswers();

        var result = await CreateSync(batchSize: 10).RunAsync(CancellationToken.None);

        Assert.Equal(10, result.Selected);
        await _gateway.ReceivedWithAnyArgs(10).FetchCharacterAsync(default!, default!, default);
    }

    [Fact]
    public async Task Run_LeavesTheRemainderForTheNextPass()
    {
        // Resumability, with no bookkeeping behind it: whatever a capped pass did not reach is still
        // stale, so the next pass selects it. The store is the progress.
        StoreHas(count: 25, lastSyncedAt: Now.AddDays(-20));
        BlizzardAnswers();

        var sync = CreateSync(batchSize: 10);

        Assert.Equal(10, (await sync.RunAsync(CancellationToken.None)).Refreshed);
        Assert.Equal(10, (await sync.RunAsync(CancellationToken.None)).Refreshed);
        Assert.Equal(5, (await sync.RunAsync(CancellationToken.None)).Refreshed);
        Assert.False((await sync.RunAsync(CancellationToken.None)).Ran);
    }

    [Fact]
    public async Task Run_WhenOneCharacterFails_StillRefreshesTheRestAndLeavesItDue()
    {
        // A batch of 200 means an occasional throttle or timeout is expected, not exceptional.
        StoreHas(count: 4, lastSyncedAt: Now.AddDays(-20));
        BlizzardAnswers();

        var failing = _store.Keys.First();

        _gateway.FetchCharacterAsync("emberfall", Key(failing), Arg.Any<CancellationToken>())
            .Returns<Task<Domain.Managers.Models.Domain.Character?>>(_ => throw new BlizzardUnavailableException("throttled"));

        var result = await CreateSync().RunAsync(CancellationToken.None);

        Assert.Equal(3, result.Refreshed);
        Assert.Equal(1, result.Failed);

        // Still due, so the next pass retries it — which is the whole of the retry story.
        Assert.Equal(Now.AddDays(-20), _store[failing]);
    }

    [Fact]
    public async Task Run_WhenBlizzardNoLongerKnowsACharacter_LeavesTheRowAloneRatherThanDeletingIt()
    {
        // A 404 means renamed, transferred or deleted. Removing other people's data on the strength of a
        // 404 is the erasure routine's job, not a sync job's.
        StoreHas(count: 2, lastSyncedAt: Now.AddDays(-20));
        _gateway.FetchCharacterAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Domain.Managers.Models.Domain.Character?)null);

        var result = await CreateSync().RunAsync(CancellationToken.None);

        Assert.Equal(2, result.Gone);
        Assert.Equal(0, result.Refreshed);
        await _repository.DidNotReceiveWithAnyArgs().UpsertCharacterAsync(default!, default, default);

        // No gear fetched either — no point asking for the equipment of a character Blizzard says is
        // not there. That is the second of the two calls per character, so it is half the budget.
        await _gateway.DidNotReceiveWithAnyArgs().FetchEquipmentAsync(default!, default!, default);
    }

    [Fact]
    public async Task Run_WithNoBlizzardCredentials_SkipsWithoutEvenQueryingForDueRows()
    {
        _gateway.IsConfigured.Returns(false);
        StoreHas(count: 5, lastSyncedAt: Now.AddDays(-20));

        var result = await CreateSync().RunAsync(CancellationToken.None);

        Assert.False(result.Ran);
        await _repository.DidNotReceiveWithAnyArgs().FindStaleAsync(default, default, default);
    }

    [Fact]
    public async Task Run_WithNothingDue_StopsAfterTheSelectionQuery()
    {
        StoreHas(count: 5, lastSyncedAt: Now);

        var result = await CreateSync().RunAsync(CancellationToken.None);

        Assert.False(result.Ran);
        await _gateway.DidNotReceiveWithAnyArgs().FetchCharacterAsync(default!, default!, default);
    }

    [Fact]
    public async Task Run_NeverHasMoreThanTheBoundInFlight()
    {
        // The batch cap is not a concurrency bound. 200 due characters all in flight at once is still 200
        // leases queued ahead of every user request, which is what BlizzardOptions.MaxQueuedCalls and
        // this bound together prevent.
        StoreHas(count: 40, lastSyncedAt: Now.AddDays(-20));

        var inFlight = 0;
        var peak = 0;
        var gate = new Lock();

        _gateway.FetchCharacterAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                lock (gate)
                {
                    inFlight++;
                    peak = Math.Max(peak, inFlight);
                }

                // Real awaiting work, so overlapping calls genuinely overlap rather than completing one
                // after another and never revealing the bound.
                await Task.Delay(5, CancellationToken.None);

                lock (gate)
                {
                    inFlight--;
                }

                return Fetched(call.ArgAt<string>(1));
            });

        _gateway.FetchEquipmentAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CharacterEquipment { LastSyncedAt = Now });

        var result = await CreateSync().RunAsync(CancellationToken.None);

        Assert.Equal(40, result.Refreshed);
        Assert.True(peak <= 4, $"Expected at most 4 concurrent refreshes, saw {peak}.");

        // And it really was concurrent — a bound that accidentally serialised everything would satisfy
        // the assertion above just as well.
        Assert.True(peak > 1, "Expected the refreshes to overlap; the pass appears to be serialised.");
    }

    private static string Key(Guid id) => id.ToString("N");

    private void StoreHas(int count, DateTimeOffset lastSyncedAt)
    {
        for (var i = 0; i < count; i++)
        {
            _store[Guid.NewGuid()] = lastSyncedAt;
        }
    }

    private void BlizzardAnswers()
    {
        _gateway.FetchCharacterAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Fetched(call.ArgAt<string>(1)));

        _gateway.FetchEquipmentAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CharacterEquipment { LastSyncedAt = Now });
    }

    // Carries back the name it was asked for, exactly as the real gateway does — which is how the upsert
    // knows which stored row it just refreshed.
    private static Domain.Managers.Models.Domain.Character Fetched(string name) => new()
    {
        Name = name,
        NameLower = name,
        BlizzardCharacterId = Random.Shared.NextInt64(1, long.MaxValue),
        LastSyncedAt = Now,
    };

    private CharacterRefreshSync CreateSync(int batchSize = 200)
    {
        // A real scope factory over the two substitutes: the job creates a scope per character because a
        // DbContext is not thread-safe, so a test that handed it services directly would not exercise
        // the shape it actually runs in.
        var services = new ServiceCollection();
        services.AddSingleton(_gateway);
        services.AddSingleton(_repository);

        return new CharacterRefreshSync(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new BlizzardStalenessPolicy(
                new OptionsWrapper<BlizzardStalenessOptions>(new BlizzardStalenessOptions()),
                new FakeTimeProvider(Now)),
            new OptionsWrapper<SyncWorkerOptions>(
                new SyncWorkerOptions { CharacterBatchSize = batchSize, MaxConcurrentRefreshes = 4 }),
            NullLogger<CharacterRefreshSync>.Instance);
    }
}
