namespace AegisScribe.Domain.Managers.Models.ViewModels;

// Puts an existing global Character on this community's roster (7.4).
//
// A character id, not a realm and name: the character has to exist in the global zone already, and one
// that Blizzard knows but we do not arrives through guild sync, which is a different act with a
// different cost. An unknown id is a 404 rather than a silent fetch.
//
// Adding somebody to the roster is also NOT claiming them — that is 7.2b's endpoint, done by the
// person whose character it is. An officer adding a character asserts nothing about who owns it.
public class AddRosterEntryViewModel
{
    public Guid CharacterId { get; set; }

    // Optional: a character may sit on the roster before the community decides what they are.
    public Guid? TenantRankId { get; set; }
}
