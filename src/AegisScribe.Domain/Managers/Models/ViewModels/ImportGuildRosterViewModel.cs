namespace AegisScribe.Domain.Managers.Models.ViewModels;

// Imports the members of a guild this community already follows onto its roster.
//
// A guild id rather than a realm and name: the guild must already be linked, and linking is what
// fetched the roster in the first place (6.6b). Importing reaches Blizzard for nothing.
public class ImportGuildRosterViewModel
{
    public Guid GuildId { get; set; }
}
