namespace AegisScribe.Domain.Managers.Models.ViewModels;

// Puts an existing global Character on this community's roster.
//
// A character id, not a realm and name: the character must already exist in the global zone, and an
// unknown id is a 404 rather than a silent fetch. Adding somebody is also NOT claiming them — an
// officer adding a character asserts nothing about who owns it.
public class AddRosterEntryViewModel
{
    public Guid CharacterId { get; set; }

    // Optional: a character may sit on the roster before the community decides what they are.
    public Guid? TenantRankId { get; set; }
}
