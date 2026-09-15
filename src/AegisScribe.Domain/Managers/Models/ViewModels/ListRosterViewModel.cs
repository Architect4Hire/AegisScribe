using AegisScribe.Domain.Managers.Models.Domain;

namespace AegisScribe.Domain.Managers.Models.ViewModels;

// No TenantId: the community comes from the route segment the middleware resolved (tenancy.md).
//
// The After* fields are the decoded cursor, not something a client fills in by hand — the controller
// unpacks the opaque cursor into them. They are re-validated here like any other input, because a
// cursor is client-supplied however opaque it looks (api-contract.md).
public class ListRosterViewModel
{
    // How many MAINS to return, not how many rows (7.4). Every alt of a returned main comes with it, so
    // a group is never split across a page boundary and the roster's ↳ rows always have their parent.
    // A page therefore holds between `Limit` and `Limit × (1 + alts)` rows.
    public int Limit { get; set; } = 25;

    public RosterSort Sort { get; set; } = RosterSort.Rank;

    // The keyset position of the last MAIN on the previous page: its sort key, then its id as the
    // tie-break. The key is a string because the sort decides what it means — a rank order, a name, an
    // item level — and the repository parses it back per sort rather than carrying three nullable
    // fields that are mutually exclusive by construction.
    public string? AfterKey { get; set; }

    public Guid? AfterId { get; set; }
}
