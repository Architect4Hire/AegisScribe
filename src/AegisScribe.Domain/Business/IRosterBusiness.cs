using AegisScribe.Domain.Data;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;

namespace AegisScribe.Domain.Business;

public interface IRosterBusiness
{
    Task<RosterPage> ListAsync(ListRosterViewModel viewModel, CancellationToken ct);

    // Puts an existing global Character on this community's roster. Null when no such character,
    // which the controller turns into a 404; throws when it is already rostered (409) or the rank is
    // not this community's (400).
    Task<Guid?> AddAsync(AddRosterEntryViewModel viewModel, CancellationToken ct);

    // False when the entry is not in this community — a 404. Throws when the rank is not this
    // community's.
    Task<bool> SetRankAsync(Guid rosterEntryId, SetRosterRankViewModel viewModel, CancellationToken ct);

    Task<bool> SetOfficerNoteAsync(Guid rosterEntryId, SetOfficerNoteViewModel viewModel, CancellationToken ct);

    // Throws when the entry still has alts (409) rather than orphaning them.
    Task<bool> RemoveAsync(Guid rosterEntryId, CancellationToken ct);

    Task<bool> LinkAltAsync(Guid rosterEntryId, LinkAltViewModel viewModel, CancellationToken ct);

    Task<bool> UnlinkAltAsync(Guid rosterEntryId, CancellationToken ct);
}
