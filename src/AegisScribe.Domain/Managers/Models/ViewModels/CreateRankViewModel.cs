namespace AegisScribe.Domain.Managers.Models.ViewModels;

// No TenantId, and there never will be one: the tenant comes from the route segment the middleware
// resolved, and a client-supplied tenant id is horizontal privilege escalation with a friendly name
// (tenancy.md). Id is absent for the same class of reason — a rank's identity is not the caller's to
// pick; Business generates it.
public class CreateRankViewModel
{
    public string Name { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public string Colour { get; set; } = string.Empty;
}
