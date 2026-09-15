namespace AegisScribe.Domain.Managers.Models.Domain;

// A character appears on a community's roster once (7.4). Refused rather than silently ignored: an
// officer who adds somebody twice has almost certainly got the wrong character, and a 204 that did
// nothing would hide that.
//
// Distinct from CharacterAlreadyClaimedException, which is about a PERSON owning a character. Being on
// the roster and being claimed are different facts with different actors, and a client branches on the
// difference — one sends you to the roster, the other to whoever holds the claim.
public class CharacterAlreadyOnRosterException()
    : Exception("That character is already on this community's roster.");
