namespace AegisScribe.Domain.Managers.Models.ViewModels;

// The character, and nothing else.
//
// There is no user id here and there never will be: a member may claim only FOR THEMSELVES, so the
// claimant comes from ICurrentUser. That is not merely a permissions nicety — external.md makes a
// claim the proof of ownership behind a user-triggered global erasure (14.1), so a field here that
// named another user would eventually be a way to delete their data.
//
// No TenantId either, for the usual reason: the community comes from the route segment the middleware
// resolved (tenancy.md).
public class ClaimCharacterViewModel
{
    public Guid CharacterId { get; set; }
}
