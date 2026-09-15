namespace AegisScribe.Domain.Managers.Models.ViewModels;

// Sets (or clears) the officer-private note on a roster entry. See SetRosterRankViewModel for why this
// is its own PUT rather than half of a PATCH — the note is exactly the field a partial PATCH would
// destroy.
//
// The content is free text written by one person about another, and it is externally-sourced as far as
// anything downstream is concerned: if it ever reaches a prompt it is untrusted input (ai.md).
public class SetOfficerNoteViewModel
{
    public string? OfficerNote { get; set; }
}
