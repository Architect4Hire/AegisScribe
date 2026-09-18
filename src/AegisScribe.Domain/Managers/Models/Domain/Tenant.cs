namespace AegisScribe.Domain.Managers.Models.Domain;

public class Tenant
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string TimeZoneId { get; set; } = null!;

    // Whether the unsolicited door is open (auth.md → "a tenant chooses which of these two doors is
    // open"). A bool rather than a two-door enum because invitations are never actually closed — an
    // invite-only community still invites — so the only real choice is whether strangers may ask.
    //
    // Closed by default: a community that has not decided should not be answering requests.
    public bool AcceptsJoinRequests { get; set; }
}
