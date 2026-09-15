using AegisScribe.Domain.Data;
using AegisScribe.Domain.Integration.Blizzard;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.SyncWorker;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace AegisScribe.Tests.Integration.Blizzard;

// 6.4b — the catalogue pass itself. The repository is exercised against real SQL by RealmRepositoryTests;
// what is pinned here is the pass's own behaviour: the restart gate, the concurrency bound, and that one
// failing group does not lose the rest.
public class RealmCatalogueSyncTests
{
    private const string Region = "us";

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-14T12:00:00Z");

    private readonly IBlizzardGateway _gateway = Substitute.For<IBlizzardGateway>();
    private readonly IRealmRepository _realms = Substitute.For<IRealmRepository>();
    private readonly FakeTimeProvider _time = new(Now);

    public RealmCatalogueSyncTests()
    {
        _gateway.IsConfigured.Returns(true);

        // No realms stored, so no region is ever "fresh" unless a test says so.
        _realms.LatestSyncedAtAsync(Region, Arg.Any<CancellationToken>()).Returns((DateTimeOffset?)null);

        _realms.ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task<int>>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task<int>>>()(CancellationToken.None));
    }

    [Fact]
    public async Task Run_FetchesEveryConnectedRealmAndUpsertsTheWholeCatalogue()
    {
        BlizzardHas(connectedRealmIds: [1, 2, 3], realmsPerGroup: 2);

        var result = await CreateSync().RunAsync(CancellationToken.None);

        Assert.True(result.Ran);
        Assert.Equal(6, result.RealmCount);
        Assert.Equal(3, result.ConnectedRealmCount);

        await _realms.Received(1).UpsertCatalogueAsync(
            Region,
            Arg.Is<IReadOnlyList<Realm>>(realms => realms.Count == 6),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_WhenTheCatalogueIsInsideTheRefreshWindow_SkipsWithoutCallingBlizzard()
    {
        // The restart gate. `aspire run` restarts this worker constantly in local development, and
        // without this every restart would spend ~100 contractual calls re-fetching an unchanged
        // catalogue.
        _realms.LatestSyncedAtAsync(Region, Arg.Any<CancellationToken>()).Returns(Now.AddDays(-1));
        BlizzardHas(connectedRealmIds: [1], realmsPerGroup: 1);

        var result = await CreateSync().RunAsync(CancellationToken.None);

        Assert.False(result.Ran);
        await _gateway.DidNotReceiveWithAnyArgs().FetchConnectedRealmIdsAsync(default!, default);
    }

    [Fact]
    public async Task Run_WhenTheCatalogueIsPastTheRefreshWindow_RunsAgain()
    {
        _realms.LatestSyncedAtAsync(Region, Arg.Any<CancellationToken>()).Returns(Now.AddDays(-8));
        BlizzardHas(connectedRealmIds: [1], realmsPerGroup: 1);

        Assert.True((await CreateSync().RunAsync(CancellationToken.None)).Ran);
    }

    [Fact]
    public async Task Run_WithNoBlizzardCredentials_SkipsWithoutTouchingTheGatewayOrTheStore()
    {
        // Missing credentials degrade, they do not crash (external.md) — the app runs on seeded data.
        _gateway.IsConfigured.Returns(false);

        var result = await CreateSync().RunAsync(CancellationToken.None);

        Assert.False(result.Ran);
        await _gateway.DidNotReceiveWithAnyArgs().FetchConnectedRealmIdsAsync(default!, default);
        await _realms.DidNotReceiveWithAnyArgs().UpsertCatalogueAsync(default!, default!, default);
    }

    [Fact]
    public async Task Run_WhenOneConnectedRealmFails_StillStoresTheRest()
    {
        // A hundred groups per pass means an occasional failure is expected, not exceptional. Losing the
        // other ninety-nine over it would make the pass less reliable the larger the region.
        BlizzardHas(connectedRealmIds: [1, 2, 3], realmsPerGroup: 2);

        _gateway.FetchConnectedRealmAsync(Region, 2, Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<Realm>>>(_ => throw new BlizzardUnavailableException("throttled"));

        var result = await CreateSync().RunAsync(CancellationToken.None);

        Assert.True(result.Ran);
        Assert.Equal(4, result.RealmCount);
    }

    [Fact]
    public async Task Run_WhenEveryConnectedRealmFails_WritesNothingRatherThanLoggingAFalseSuccess()
    {
        BlizzardHas(connectedRealmIds: [1, 2], realmsPerGroup: 2);

        _gateway.FetchConnectedRealmAsync(Region, Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<Realm>>>(_ => throw new BlizzardUnavailableException("down"));

        var result = await CreateSync().RunAsync(CancellationToken.None);

        Assert.False(result.Ran);
        await _realms.DidNotReceiveWithAnyArgs().UpsertCatalogueAsync(default!, default!, default);
    }

    [Fact]
    public async Task Run_WhenTheIndexIsEmpty_DoesNotWriteAnEmptyCatalogue()
    {
        _gateway.FetchConnectedRealmIdsAsync(Region, Arg.Any<CancellationToken>()).Returns([]);

        Assert.False((await CreateSync().RunAsync(CancellationToken.None)).Ran);
        await _realms.DidNotReceiveWithAnyArgs().UpsertCatalogueAsync(default!, default!, default);
    }

    [Fact]
    public async Task Run_NeverHasMoreThanFourConnectedRealmFetchesInFlight()
    {
        // Not a throughput control — the rate limiter already holds outbound calls to 9/second. What this
        // bounds is queue occupancy: an unbounded fan-out parks ~100 leases in a queue 200 deep, and a
        // user's character lookup then waits behind the entire realm catalogue.
        var ids = Enumerable.Range(1, 40).Select(i => (long)i).ToList();
        _gateway.FetchConnectedRealmIdsAsync(Region, Arg.Any<CancellationToken>()).Returns(ids);

        var inFlight = 0;
        var peak = 0;
        var gate = new Lock();

        _gateway.FetchConnectedRealmAsync(Region, Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                lock (gate)
                {
                    inFlight++;
                    peak = Math.Max(peak, inFlight);
                }

                // Real awaiting work, so overlapping calls genuinely overlap rather than completing
                // synchronously one after another and never revealing the bound.
                await Task.Delay(5, CancellationToken.None);

                lock (gate)
                {
                    inFlight--;
                }

                return (IReadOnlyList<Realm>)[Realm(call.ArgAt<long>(1), 1)];
            });

        var result = await CreateSync().RunAsync(CancellationToken.None);

        Assert.Equal(40, result.RealmCount);
        Assert.True(peak <= 4, $"Expected at most 4 concurrent fetches, saw {peak}.");

        // And it really was concurrent — a bound that accidentally serialised everything would also
        // satisfy the assertion above.
        Assert.True(peak > 1, "Expected the fetches to overlap; the pass appears to be serialised.");
    }

    private void BlizzardHas(long[] connectedRealmIds, int realmsPerGroup)
    {
        _gateway.FetchConnectedRealmIdsAsync(Region, Arg.Any<CancellationToken>()).Returns(connectedRealmIds);

        foreach (var connectedRealmId in connectedRealmIds)
        {
            _gateway.FetchConnectedRealmAsync(Region, connectedRealmId, Arg.Any<CancellationToken>())
                .Returns((IReadOnlyList<Realm>)
                    [.. Enumerable.Range(1, realmsPerGroup).Select(i => Realm(connectedRealmId, i))]);
        }
    }

    private static Realm Realm(long connectedRealmId, int index) => new()
    {
        Region = Region,
        Slug = $"realm-{connectedRealmId}-{index}",
        Name = $"Realm {connectedRealmId}-{index}",
        BlizzardRealmId = (connectedRealmId * 100) + index,
        BlizzardConnectedRealmId = connectedRealmId,
        LastSyncedAt = Now,
    };

    private RealmCatalogueSync CreateSync() => new(
        _gateway,
        _realms,
        new OptionsWrapper<BlizzardOptions>(new BlizzardOptions { Region = Region }),
        new OptionsWrapper<BlizzardStalenessOptions>(new BlizzardStalenessOptions()),
        _time,
        NullLogger<RealmCatalogueSync>.Instance);
}
