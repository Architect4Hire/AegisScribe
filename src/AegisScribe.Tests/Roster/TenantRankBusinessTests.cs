using AegisScribe.Domain.Business;
using AegisScribe.Domain.Data;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;
using NSubstitute;

namespace AegisScribe.Tests.Roster;

// Business: mocked data layer. What is under test is the duplicate-name RULE and the VM<->domain
// translation either side of it — nothing here touches EF, a cache or HTTP.
public class TenantRankBusinessTests
{
    private readonly ITenantRankDataLayer _dataLayer = Substitute.For<ITenantRankDataLayer>();
    private readonly TenantRankBusiness _business;

    public TenantRankBusinessTests()
    {
        _business = new TenantRankBusiness(_dataLayer);
    }

    [Fact]
    public async Task Create_TranslatesTheViewModel_AndNeverAssignsTenantId()
    {
        TenantRank? saved = null;
        _dataLayer
            .When(layer => layer.AddAsync(Arg.Any<TenantRank>(), Arg.Any<CancellationToken>()))
            .Do(call => saved = call.Arg<TenantRank>());

        var result = await _business.CreateAsync(
            new CreateRankViewModel { Name = "Raider", SortOrder = 10, Colour = "#cba76a" }, CancellationToken.None);

        Assert.NotNull(saved);
        Assert.Equal("Raider", saved!.Name);
        Assert.Equal(10, saved.SortOrder);
        Assert.Equal("#cba76a", saved.Colour);
        Assert.NotEqual(Guid.Empty, saved.Id);

        // Left at Guid.Empty on purpose: the SaveChanges interceptor stamps it from the ambient
        // context, and business code that assigned it would be the bug tenancy.md describes.
        Assert.Equal(Guid.Empty, saved.TenantId);

        Assert.Equal(saved.Id, result.Id);
        Assert.Equal("Raider", result.Name);
    }

    [Fact]
    public async Task Create_DuplicateName_Throws_AndNeverWrites()
    {
        _dataLayer.NameExistsAsync("Raider", null, Arg.Any<CancellationToken>()).Returns(true);

        var ex = await Assert.ThrowsAsync<RankNameTakenException>(() => _business.CreateAsync(
            new CreateRankViewModel { Name = "Raider", SortOrder = 10, Colour = "#cba76a" }, CancellationToken.None));

        Assert.Equal("Raider", ex.Name);
        await _dataLayer.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task Update_UnknownRank_ReturnsNull_AndNeverWrites()
    {
        _dataLayer.FindAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((TenantRank?)null);

        // "No such rank" and "that rank is another community's" arrive here identically, because the
        // query filter one layer down makes the second look like the first. Both must stay a 404.
        var result = await _business.UpdateAsync(
            Guid.NewGuid(), new UpdateRankViewModel { Name = "Raider", SortOrder = 10, Colour = "#cba76a" }, CancellationToken.None);

        Assert.Null(result);
        await _dataLayer.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Fact]
    public async Task Update_KeepingItsOwnName_IsAllowed()
    {
        var rankId = Guid.NewGuid();
        var existing = new TenantRank { Id = rankId, Name = "Raider", SortOrder = 10, Colour = "#cba76a" };
        _dataLayer.FindAsync(rankId, Arg.Any<CancellationToken>()).Returns(existing);
        // The collision check excludes the row being edited; anything else and a recolour of an
        // unchanged name would collide with itself.
        _dataLayer.NameExistsAsync("Raider", rankId, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _business.UpdateAsync(
            rankId, new UpdateRankViewModel { Name = "Raider", SortOrder = 5, Colour = "#3e9c77" }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(5, result!.SortOrder);
        Assert.Equal("#3e9c77", result.Colour);
        await _dataLayer.Received(1).UpdateAsync(existing, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_RenamingOntoAnotherRanksName_Throws_AndNeverWrites()
    {
        var rankId = Guid.NewGuid();
        _dataLayer.FindAsync(rankId, Arg.Any<CancellationToken>())
            .Returns(new TenantRank { Id = rankId, Name = "Trial", SortOrder = 20, Colour = "#616d7e" });
        _dataLayer.NameExistsAsync("Raider", rankId, Arg.Any<CancellationToken>()).Returns(true);

        await Assert.ThrowsAsync<RankNameTakenException>(() => _business.UpdateAsync(
            rankId, new UpdateRankViewModel { Name = "Raider", SortOrder = 20, Colour = "#616d7e" }, CancellationToken.None));

        await _dataLayer.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Fact]
    public async Task Delete_WithNoHolders_DoesNotLookTheRankUpFirst()
    {
        var rankId = Guid.NewGuid();
        _dataLayer.CountRankHoldersAsync(rankId, Arg.Any<CancellationToken>()).Returns(0);

        await _business.DeleteAsync(rankId, CancellationToken.None);

        // Still no existence check: DELETE is idempotent (api-contract.md), and the query filter is
        // what makes another community's id delete nothing rather than theirs. A Find here would turn
        // that into an existence oracle for rows the caller cannot see — the holder count is not one,
        // because it is query-filtered and reads zero for anything outside this community.
        await _dataLayer.DidNotReceiveWithAnyArgs().FindAsync(default, default);
        await _dataLayer.Received(1).DeleteAsync(rankId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_WithHolders_Throws_AndNeverDeletes()
    {
        // 7.2's rule. The alternative — letting the delete through and clearing the rank off every
        // holder — is the silent kind of damage: an officer tidying up a ladder un-ranks forty raiders
        // and the 204 says nothing about it.
        var rankId = Guid.NewGuid();
        _dataLayer.CountRankHoldersAsync(rankId, Arg.Any<CancellationToken>()).Returns(12);

        var ex = await Assert.ThrowsAsync<RankInUseException>(
            () => _business.DeleteAsync(rankId, CancellationToken.None));

        // The count travels with the exception because "reassign these first" is only actionable if
        // the officer knows how many there are.
        Assert.Equal(12, ex.HolderCount);
        await _dataLayer.DidNotReceiveWithAnyArgs().DeleteAsync(default, default);
    }

    [Fact]
    public async Task List_PassesTheProjectedSummariesThrough()
    {
        var expected = new List<TenantRankServiceModel> { new() { Name = "Raider" } };
        _dataLayer.ListAsync(Arg.Any<CancellationToken>()).Returns(expected);

        var result = await _business.ListAsync(CancellationToken.None);

        Assert.Same(expected, result);
    }
}
