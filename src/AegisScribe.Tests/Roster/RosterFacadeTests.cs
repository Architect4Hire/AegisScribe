using AegisScribe.Domain.Business;
using AegisScribe.Domain.Data;
using AegisScribe.Domain.Facade;
using AegisScribe.Domain.Managers.Models.Domain;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;
using AegisScribe.Domain.Managers.Validators;
using FluentValidation;
using NSubstitute;

namespace AegisScribe.Tests.Roster;

// Facade: real validators, mocked business. There is no cache trio here, and that is the point worth
// pinning — this read is deliberately NOT cached, unlike the rank ladder and the claim state. What the
// facade owes instead is the input discipline: the limit cap, the sort whitelist, and bounds on the
// cursor's decoded parts.
public class RosterFacadeTests
{
    private readonly IRosterBusiness _business = Substitute.For<IRosterBusiness>();
    private readonly RosterFacade _facade;

    public RosterFacadeTests()
    {
        _facade = new RosterFacade(
            _business,
            new ListRosterViewModelValidator(),
            new LinkAltViewModelValidator(),
            new AddRosterEntryViewModelValidator(),
            new ImportGuildRosterViewModelValidator(),
            new SetRosterRankViewModelValidator(),
            new SetOfficerNoteViewModelValidator());
    }

    [Fact]
    public async Task List_Valid_Delegates()
    {
        var viewModel = new ListRosterViewModel { Limit = 25 };
        var expected = new RosterPage([new RosterEntryServiceModel { CharacterName = "Aldric" }], 1);
        _business.ListAsync(viewModel, Arg.Any<CancellationToken>()).Returns(expected);

        var result = await _facade.ListAsync(viewModel, CancellationToken.None);

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task List_LimitOverTheCap_ThrowsValidationException_AndNeverReachesBusiness()
    {
        // The server-enforced maximum api-contract.md requires. Lower than the character search's 100,
        // because this limit counts MAINS and each can drag several alts along with it.
        var viewModel = new ListRosterViewModel { Limit = 5000 };

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => _facade.ListAsync(viewModel, CancellationToken.None));

        Assert.Contains(ex.Errors, e => e.PropertyName == nameof(ListRosterViewModel.Limit));
        await _business.DidNotReceiveWithAnyArgs().ListAsync(default!, default);
    }

    [Fact]
    public async Task List_SortOutsideTheWhitelist_ThrowsValidationException()
    {
        // The value reaches an ORDER BY, so the set is closed and anything outside it is refused rather
        // than defaulted — a page quietly returned in a different order is how a paging bug gets blamed
        // on the server.
        var viewModel = new ListRosterViewModel { Sort = (RosterSort)999 };

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => _facade.ListAsync(viewModel, CancellationToken.None));

        Assert.Contains(ex.Errors, e => e.PropertyName == nameof(ListRosterViewModel.Sort));
    }

    [Fact]
    public async Task List_OverlongCursorKey_IsRejected()
    {
        // A cursor is client-supplied however opaque it looks (api-contract.md).
        var viewModel = new ListRosterViewModel
        {
            AfterKey = new string('a', 200),
            AfterId = Guid.NewGuid(),
        };

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => _facade.ListAsync(viewModel, CancellationToken.None));

        Assert.Contains(ex.Errors, e => e.PropertyName == nameof(ListRosterViewModel.AfterKey));
    }

    [Fact]
    public async Task SetOfficerNote_OverlongNote_IsRejected_AndNeverReachesBusiness()
    {
        var viewModel = new SetOfficerNoteViewModel { OfficerNote = new string('x', 2000) };

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => _facade.SetOfficerNoteAsync(Guid.NewGuid(), viewModel, CancellationToken.None));

        Assert.Contains(ex.Errors, e => e.PropertyName == nameof(SetOfficerNoteViewModel.OfficerNote));
        await _business.DidNotReceiveWithAnyArgs().SetOfficerNoteAsync(default, default!, default);
    }

    [Fact]
    public async Task SetOfficerNote_Null_IsValid_AndMeansClear()
    {
        await _facade.SetOfficerNoteAsync(
            Guid.NewGuid(), new SetOfficerNoteViewModel { OfficerNote = null }, CancellationToken.None);

        await _business.ReceivedWithAnyArgs(1).SetOfficerNoteAsync(default, default!, default);
    }

    [Fact]
    public async Task Add_EmptyCharacterId_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => _facade.AddAsync(new AddRosterEntryViewModel { CharacterId = Guid.Empty }, CancellationToken.None));

        Assert.Contains(ex.Errors, e => e.PropertyName == nameof(AddRosterEntryViewModel.CharacterId));
    }

    [Fact]
    public async Task Import_EmptyGuildId_IsRejectedBeforeBusiness()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => _facade.ImportFromGuildAsync(
                new ImportGuildRosterViewModel { GuildId = Guid.Empty }, CancellationToken.None));

        Assert.Contains(ex.Errors, e => e.PropertyName == nameof(ImportGuildRosterViewModel.GuildId));

        await _business.DidNotReceive().ImportFromGuildAsync(
            Arg.Any<ImportGuildRosterViewModel>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Import_Valid_Delegates()
    {
        var viewModel = new ImportGuildRosterViewModel { GuildId = Guid.NewGuid() };
        var expected = new RosterImportServiceModel { Imported = 3, AlreadyOnRoster = 1 };
        _business.ImportFromGuildAsync(viewModel, Arg.Any<CancellationToken>()).Returns(expected);

        Assert.Same(expected, await _facade.ImportFromGuildAsync(viewModel, CancellationToken.None));
    }
}
