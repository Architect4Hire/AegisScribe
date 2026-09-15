namespace AegisScribe.Domain.Managers.Models.ViewModels;

// Sets (or clears) the officer-private note. Its own PUT rather than half of a PATCH — see
// SetRosterRankViewModel; the note is exactly the field a partial PATCH would destroy.
//
// Free text written by one person about another: if it ever reaches a prompt it is untrusted (ai.md).
public class SetOfficerNoteViewModel
{
    public string? OfficerNote { get; set; }
}
