using AegisScribe.Domain.Business;
using AegisScribe.Domain.Context;
using AegisScribe.Domain.Data;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;
using NSubstitute;

namespace AegisScribe.Tests.Roster;

// Business against a mocked data layer. Two things are worth unit-testing here: that the keyset
// position and sort pass down INTACT rather than quietly defaulting, and that the officer-note flag is
// decided from the caller's ROLE.
//
// The write rules turn on the interaction between an entry, a claim and a membership role, so they are
// exercised end-to-end in RosterWriteTests instead — stubbing all three would prove the stubs agree
// with each other rather than that the rule holds.
public class RosterBusinessTests
{
    private const string MeUserId = "user-me";
    private static readonly Guid TenantId = Guid.NewGuid();

    private readonly IRosterEntryDataLayer _dataLayer = Substitute.For<IRosterEntryDataLayer>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly RosterBusiness _business;

    public RosterBusinessTests()
    {
        _currentUser.UserId.Returns(MeUserId);

        var tenantContext = new TenantContext();
        tenantContext.SetTenant(TenantId);

        _business = new RosterBusiness(_dataLayer, _currentUser, tenantContext, TimeProvider.System);
    }

    [Fact]
    public async Task List_ForwardsTheWholeKeysetPositionAndSort()
    {
        var afterId = Guid.NewGuid();
        var viewModel = new ListRosterViewModel
        {
            Limit = 50,
            Sort = RosterSort.ItemLevel,
            AfterKey = "639",
            AfterId = afterId,
        };
        _dataLayer.ListAsync(
                Arg.Any<RosterSort>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new RosterPage([], 0));

        await _business.ListAsync(viewModel, CancellationToken.None);

        // Every term, or the page silently restarts from the top — the failure mode a cursor exists to
        // prevent, and one that reads as a server bug rather than a dropped argument.
        await _dataLayer.Received(1).ListAsync(
            RosterSort.ItemLevel, "639", afterId, 50, Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task List_AsAMember_AsksForThePageWithoutOfficerNotes()
    {
        _dataLayer.GetTenantRoleAsync(TenantId, MeUserId, Arg.Any<CancellationToken>()).Returns(TenantRole.Member);
        _dataLayer.ListAsync(
                Arg.Any<RosterSort>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new RosterPage([], 0));

        await _business.ListAsync(new ListRosterViewModel(), CancellationToken.None);

        // False, so the note is blanked in the SQL projection: an officer-private note never leaves the
        // database on a request that had no business reading it.
        await _dataLayer.Received(1).ListAsync(
            Arg.Any<RosterSort>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<int>(),
            includeOfficerNote: false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task List_AsAnOfficer_AsksForThePageWithOfficerNotes()
    {
        _dataLayer.GetTenantRoleAsync(TenantId, MeUserId, Arg.Any<CancellationToken>()).Returns(TenantRole.Officer);
        _dataLayer.ListAsync(
                Arg.Any<RosterSort>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new RosterPage([], 0));

        await _business.ListAsync(new ListRosterViewModel(), CancellationToken.None);

        await _dataLayer.Received(1).ListAsync(
            Arg.Any<RosterSort>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<int>(),
            includeOfficerNote: true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task List_PassesThePageThrough()
    {
        var expected = new RosterPage([new RosterEntryServiceModel { CharacterName = "Aldric" }], 1);
        _dataLayer.ListAsync(
                Arg.Any<RosterSort>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(expected);

        var result = await _business.ListAsync(new ListRosterViewModel(), CancellationToken.None);

        Assert.Same(expected, result);
    }
}
