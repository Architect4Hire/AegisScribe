namespace AegisScribe.Domain.Managers.Models.Domain;

// A character appears on a community's roster once. Refused rather than silently ignored: an officer
// who adds somebody twice has almost certainly got the wrong character, and a no-op 204 would hide it.
//
// Distinct from CharacterAlreadyClaimedException, which is about a PERSON owning a character. A client
// branches on the difference — one sends you to the roster, the other to whoever holds the claim.
public class CharacterAlreadyOnRosterException()
    : Exception("That character is already on this community's roster.");
