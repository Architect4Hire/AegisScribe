using AegisScribe.Domain.Business;
using AegisScribe.Domain.Data;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;
using FluentValidation;

namespace AegisScribe.Domain.Facade;

// Deliberately NOT cached, for two independent reasons:
//
//   - a paginated read's key would have to include the sort, cursor and limit — unbounded cardinality
//     for a page that is cheap to recompute;
//   - the page carries OfficerNote, which is populated per CALLER, so a tenant-keyed entry would serve
//     the first officer's view to every member of the community.
//
// Validation still belongs here: Limit carries the server-enforced maximum, Sort is a closed whitelist,
// and the cursor's decoded parts are client-supplied like any other input.
public class RosterFacade(
    IRosterBusiness business,
    IValidator<ListRosterViewModel> listValidator,
    IValidator<LinkAltViewModel> linkAltValidator,
    IValidator<AddRosterEntryViewModel> addValidator,
    IValidator<ImportGuildRosterViewModel> importValidator,
    IValidator<SetRosterRankViewModel> rankValidator,
    IValidator<SetOfficerNoteViewModel> noteValidator) : IRosterFacade
{
    public async Task<RosterPage> ListAsync(ListRosterViewModel viewModel, CancellationToken ct)
    {
        await listValidator.ValidateAndThrowAsync(viewModel, ct);

        return await business.ListAsync(viewModel, ct);
    }

    public async Task<Guid?> AddAsync(AddRosterEntryViewModel viewModel, CancellationToken ct)
    {
        await addValidator.ValidateAndThrowAsync(viewModel, ct);

        return await business.AddAsync(viewModel, ct);
    }

    // Not cached, and nothing to invalidate either: the roster read below is uncached for the reasons
    // at the top of this file, so an import has no stale page to leave behind.
    public async Task<RosterImportServiceModel?> ImportFromGuildAsync(
        ImportGuildRosterViewModel viewModel, CancellationToken ct)
    {
        await importValidator.ValidateAndThrowAsync(viewModel, ct);

        return await business.ImportFromGuildAsync(viewModel, ct);
    }

    public async Task<bool> SetRankAsync(
        Guid rosterEntryId, SetRosterRankViewModel viewModel, CancellationToken ct)
    {
        await rankValidator.ValidateAndThrowAsync(viewModel, ct);

        return await business.SetRankAsync(rosterEntryId, viewModel, ct);
    }

    public async Task<bool> SetOfficerNoteAsync(
        Guid rosterEntryId, SetOfficerNoteViewModel viewModel, CancellationToken ct)
    {
        await noteValidator.ValidateAndThrowAsync(viewModel, ct);

        return await business.SetOfficerNoteAsync(rosterEntryId, viewModel, ct);
    }

    public Task<bool> RemoveAsync(Guid rosterEntryId, CancellationToken ct) =>
        business.RemoveAsync(rosterEntryId, ct);

    public async Task<bool> LinkAltAsync(Guid rosterEntryId, LinkAltViewModel viewModel, CancellationToken ct)
    {
        await linkAltValidator.ValidateAndThrowAsync(viewModel, ct);

        return await business.LinkAltAsync(rosterEntryId, viewModel, ct);
    }

    public Task<bool> UnlinkAltAsync(Guid rosterEntryId, CancellationToken ct) =>
        business.UnlinkAltAsync(rosterEntryId, ct);
}
