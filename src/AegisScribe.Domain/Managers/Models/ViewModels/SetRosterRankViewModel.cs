namespace AegisScribe.Domain.Managers.Models.ViewModels;

// Sets (or clears) a roster entry's rank. Its own endpoint rather than half of a PATCH, and that is a
// data-safety choice: JSON cannot distinguish "field omitted" from "field set to null", so a combined
// rank-and-note PATCH from a UI that only edits rank would silently wipe an officer's note.
public class SetRosterRankViewModel
{
    public Guid? TenantRankId { get; set; }
}
