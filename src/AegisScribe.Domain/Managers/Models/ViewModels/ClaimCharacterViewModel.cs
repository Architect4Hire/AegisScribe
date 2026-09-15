namespace AegisScribe.Domain.Managers.Models.ViewModels;

// The character, and nothing else.
//
// No user id here, and there never will be: a claim is the proof of ownership behind a user-triggered
// global erasure (external.md), so a field naming another user would be a way to delete their data.
// The claimant comes from ICurrentUser, and the community from the route (tenancy.md).
public class ClaimCharacterViewModel
{
    public Guid CharacterId { get; set; }
}
