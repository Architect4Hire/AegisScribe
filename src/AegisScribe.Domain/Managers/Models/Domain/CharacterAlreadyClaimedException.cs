namespace AegisScribe.Domain.Managers.Models.Domain;

// A second claim on an already-claimed character is refused, never silently overwritten.
//
// Carries the holder's display name so the conflict dialog can say who without a second round-trip;
// null when they have set none. Note what it does NOT carry: the holder's user id or email. The display
// name is already visible to any member through the claim GET, so naming it discloses nothing new —
// anything more would.
public class CharacterAlreadyClaimedException(string? claimedByDisplayName)
    : Exception("That character is already claimed by another member of this community.")
{
    public string? ClaimedByDisplayName { get; } = claimedByDisplayName;
}
