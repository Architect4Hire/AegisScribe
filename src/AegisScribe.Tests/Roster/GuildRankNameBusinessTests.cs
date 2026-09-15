using AegisScribe.Domain.Business;
using AegisScribe.Domain.Data;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;
using NSubstitute;

namespace AegisScribe.Tests.Roster;

// Business: mocked data layer. One rule lives here, and it is a rule by the add-endpoint skill's own
// test — delete the follows-guild check and an officer can name the ranks of a guild their community
// has nothing to do with, which is a write that should have been refused happening rather than an
// answer that is merely stale.
public class GuildRankNameBusinessTests
{
    private readonly IGuildRankNameDataLayer _dataLayer = Substitute.For<IGuildRankNameDataLayer>();
    private readonly GuildRankNameBusiness _business;

    public GuildRankNameBusinessTests()
    {
        _business = new GuildRankNameBusiness(_dataLayer);
    }

    [Fact]
    public async Task Set_ForAFollowedGuild_Writes()
    {
        var guildId = Guid.NewGuid();
        _dataLayer.FollowsGuildAsync(guildId, Arg.Any<CancellationToken>()).Returns(true);

        var set = await _business.SetAsync(
            guildId, 3, new SetGuildRankNameViewModel { Name = "Veteran" }, CancellationToken.None);

        Assert.True(set);
        await _dataLayer.Received(1).SetAsync(guildId, 3, "Veteran", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Set_ForAGuildTheCommunityDoesNotFollow_ReturnsFalse_AndNeverWrites()
    {
        // The guard. Without it the row would be written and then be invisible, because nothing joins
        // to a guild this community does not follow — a write that succeeds and does nothing.
        var guildId = Guid.NewGuid();
        _dataLayer.FollowsGuildAsync(guildId, Arg.Any<CancellationToken>()).Returns(false);

        var set = await _business.SetAsync(
            guildId, 3, new SetGuildRankNameViewModel { Name = "Veteran" }, CancellationToken.None);

        // False becomes a 404 at the controller — the same answer a guild that does not exist gets,
        // because the query filter makes the two indistinguishable and they should stay that way.
        Assert.False(set);
        await _dataLayer.DidNotReceiveWithAnyArgs().SetAsync(default, default, default, default);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10)]
    [InlineData(int.MaxValue)]
    public async Task Set_ARankOutsideZeroToNine_Throws_AndNeverReachesTheGuildCheck(int rank)
    {
        // 0-9 is the range Blizzard's roster reports, so anything else is a malformed request rather
        // than a missing thing — a field error, not a 404. Checked before the guild lookup, so a
        // nonsense rank costs no query.
        var ex = await Assert.ThrowsAsync<DomainValidationException>(() => _business.SetAsync(
            Guid.NewGuid(), rank, new SetGuildRankNameViewModel { Name = "Veteran" }, CancellationToken.None));

        Assert.Contains("rank", ex.Errors.Keys);
        await _dataLayer.DidNotReceiveWithAnyArgs().FollowsGuildAsync(default, default);
    }

    [Fact]
    public async Task Set_ANullName_Clears()
    {
        var guildId = Guid.NewGuid();
        _dataLayer.FollowsGuildAsync(guildId, Arg.Any<CancellationToken>()).Returns(true);

        await _business.SetAsync(guildId, 3, new SetGuildRankNameViewModel { Name = null }, CancellationToken.None);

        // Passed straight through as null. "Never named" and "named then cleared" are one state at
        // rest, so the UI has one blank to render rather than two that look alike.
        await _dataLayer.Received(1).SetAsync(guildId, 3, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task List_PassesTheProjectedRowsThrough()
    {
        var expected = new List<GuildRankNameServiceModel> { new() { Rank = 0, Name = "Guild Master" } };
        _dataLayer.ListAsync(Arg.Any<CancellationToken>()).Returns(expected);

        Assert.Same(expected, await _business.ListAsync(CancellationToken.None));
    }
}
