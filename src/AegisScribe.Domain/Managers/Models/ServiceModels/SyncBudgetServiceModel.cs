namespace AegisScribe.Domain.Managers.Models.ServiceModels;

// What a tenant Owner sees. external.md is explicit that the budget is visible to the owner, for a
// plain reason: a limit nobody can see reads as a bug when it bites.
public class SyncBudgetServiceModel
{
    public int CallsPerWindow { get; set; }

    public int CallsConsumed { get; set; }

    public int CallsRemaining { get; set; }

    // ISO 8601 with an explicit offset on the wire (api-contract.md). The client renders it in the
    // device's zone; the API has no opinion about the viewer's.
    public DateTimeOffset WindowResetsAt { get; set; }
}
