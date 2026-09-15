namespace AegisScribe.Domain.Managers.Models.Domain;

// A member tried to release somebody else's claim. Lives in Business rather than a policy because
// answering it requires reading the row first — policies answer "what rank are you here", Business
// answers "is this yours" (auth.md). An officer wanting this outcome has their own audited route.
public class ClaimNotYoursException()
    : Exception("That claim belongs to another member of this community.");
