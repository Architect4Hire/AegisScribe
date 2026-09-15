namespace AegisScribe.Domain.Managers.Models.ViewModels;

// The main to attach this entry to. The entry being changed is the route segment, so it is not here —
// which is also why the self-link check cannot be a validator rule and lives in Business instead.
public class LinkAltViewModel
{
    public Guid MainRosterEntryId { get; set; }
}
