namespace AegisScribe.Domain.Managers.Models.ViewModels;

// A stranger asking to join. The community comes from the SLUG in the route and nothing else — a
// tenant id in a body is a horizontal privilege escalation with a friendly name (tenancy.md).
public class CreateJoinRequestViewModel
{
    public string? Message { get; set; }
}
