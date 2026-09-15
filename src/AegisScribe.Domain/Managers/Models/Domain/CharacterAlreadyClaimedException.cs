namespace AegisScribe.Domain.Managers.Models.Domain;

// A second claim on an already-claimed character is refused, never silently overwritten (7.2b). A
// rule by the add-endpoint skill's own worked table — "read the existing claim before inserting a new
// one: delete the check and a character gets two owners, a refusal that should have happened didn't".
//
// Carries the holder's display name because the 409 body surfaces it, so the conflict dialog can say
// who without a second round-trip. Null when the holder has set no display name; the client falls back
// to "already claimed" rather than rendering an empty string.
//
// Note what it does NOT carry: the holder's user id or email. The display name is already visible to
// any member through the claim GET, so naming it here discloses nothing new — anything more would.
public class CharacterAlreadyClaimedException(string? claimedByDisplayName)
    : Exception("That character is already claimed by another member of this community.")
{
    public string? ClaimedByDisplayName { get; } = claimedByDisplayName;
}
