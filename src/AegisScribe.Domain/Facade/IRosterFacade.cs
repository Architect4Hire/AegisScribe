using AegisScribe.Domain.Data;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;

namespace AegisScribe.Domain.Facade;

public interface IRosterFacade
{
    Task<RosterPage> ListAsync(ListRosterViewModel viewModel, CancellationToken ct);

    Task<Guid?> AddAsync(AddRosterEntryViewModel viewModel, CancellationToken ct);

    Task<RosterImportServiceModel?> ImportFromGuildAsync(
        ImportGuildRosterViewModel viewModel, CancellationToken ct);

    Task<bool> SetRankAsync(Guid rosterEntryId, SetRosterRankViewModel viewModel, CancellationToken ct);

    Task<bool> SetOfficerNoteAsync(Guid rosterEntryId, SetOfficerNoteViewModel viewModel, CancellationToken ct);

    Task<bool> RemoveAsync(Guid rosterEntryId, CancellationToken ct);

    Task<bool> LinkAltAsync(Guid rosterEntryId, LinkAltViewModel viewModel, CancellationToken ct);

    Task<bool> UnlinkAltAsync(Guid rosterEntryId, CancellationToken ct);
}
