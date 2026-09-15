namespace AegisScribe.Domain.Managers.Models.ViewModels;

// The same shape as CreateRankViewModel today, and deliberately a separate type rather than a shared
// one. These are two independent promises in a versioned contract (api-contract.md): the day an
// update gains a field a create must not accept — or a create gains a required field an update must
// not — a shared type makes that a breaking change to both. The shape rules they have in common live
// once, in RankValidationRules.
//
// A full-shape body, because the route is PUT: the whole rank is replaced, which is what makes the
// verb naturally idempotent and safe for a mobile client to retry.
public class UpdateRankViewModel
{
    public string Name { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public string Colour { get; set; } = string.Empty;
}
