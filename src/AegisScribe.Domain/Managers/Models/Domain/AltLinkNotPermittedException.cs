namespace AegisScribe.Domain.Managers.Models.Domain;

// A member tried to reorganise characters they do not claim.
//
// Resource authorization, so it lives in Business and not in a policy — answering it means reading the
// caller's claims first. Policies answer "what rank are you here"; Business answers "is this yours"
// (auth.md). An officer passes this check by rank instead, and their action is audited.
public class AltLinkNotPermittedException()
    : Exception("You may only link alts among characters you have claimed.");
