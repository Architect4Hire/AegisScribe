namespace AegisScribe.Domain.Managers.Models.Domain;

// A member tried to release somebody else's claim (7.2b).
//
// This is the add-endpoint skill's canonical resource-authorization case, named there verbatim: "this
// claim belongs to another user" throws a domain exception the global handler maps to 403. It lives in
// Business rather than in a policy because answering it requires reading the row first — policies
// answer "what rank are you here", Business answers "is this yours" (auth.md).
//
// An officer wanting this outcome has their own route, which frees the claim and writes an audit row.
public class ClaimNotYoursException()
    : Exception("That claim belongs to another member of this community.");
