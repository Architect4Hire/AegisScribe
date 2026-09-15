namespace AegisScribe.Domain.Managers.Models.ViewModels;

// Sets (or clears) a roster entry's rank.
//
// Its own endpoint rather than half of a PATCH, and that is a data-safety choice: JSON cannot
// distinguish "field omitted" from "field set to null", so a combined rank-and-note PATCH sent by a UI
// that only edits rank would silently wipe an officer's note. A PUT whose body is exactly the one
// field it changes has no such ambiguity — null means clear, and nothing else is affected.
public class SetRosterRankViewModel
{
    public Guid? TenantRankId { get; set; }
}
