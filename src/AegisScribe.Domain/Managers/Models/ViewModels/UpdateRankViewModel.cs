namespace AegisScribe.Domain.Managers.Models.ViewModels;

// The same shape as CreateRankViewModel today, deliberately kept separate: these are two independent
// promises in a versioned contract, and the day one gains a field the other must not accept, a shared
// type makes that breaking for both. Common shape rules live once, in RankValidationRules.
//
// A full-shape body because the route is PUT — the whole rank is replaced, which is what makes it
// naturally idempotent and safe for a mobile client to retry.
public class UpdateRankViewModel
{
    public string Name { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public string Colour { get; set; } = string.Empty;
}
