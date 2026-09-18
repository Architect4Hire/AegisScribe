namespace AegisScribe.Domain.Managers.Models.Domain;

// Somebody already has an outstanding request to this community. Business pre-checks it for the
// readable answer; the filtered unique index is the authority under a race.
public class JoinRequestAlreadyPendingException()
    : Exception("You already have a request to join this community awaiting a decision.");
