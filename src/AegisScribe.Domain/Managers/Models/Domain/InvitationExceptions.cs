namespace AegisScribe.Domain.Managers.Models.Domain;

// No such token, anywhere. Deliberately a different shape from the three below: a token that never
// existed must stay indistinguishable from nothing at all, while the three dead states are
// distinguishable TO THE HOLDER on purpose — somebody who was genuinely invited deserves to know
// whether they are late, second, or unwanted.
public class InvitationNotFoundException() : Exception("That invitation link is not valid.");

// Why a real invitation cannot be used. Each maps to its own problem type, because the thing the
// holder should do next differs: ask for a fresh link, nothing, or talk to an officer.
public enum InvitationRefusal
{
    Expired,
    Consumed,
    Revoked,
}

public class InvitationUnusableException(InvitationRefusal reason) : Exception(Describe(reason))
{
    public InvitationRefusal Reason { get; } = reason;

    // None of these names the community. A dead token buys its holder nothing, including the
    // knowledge of what it was for.
    private static string Describe(InvitationRefusal reason) => reason switch
    {
        InvitationRefusal.Expired => "That invitation has expired.",
        InvitationRefusal.Consumed => "That invitation has already been used.",
        _ => "That invitation has been revoked.",
    };
}

// The (TenantId, UserId) primary key refused a second membership. Business pre-checks it wherever it
// can; the key is the authority when two paths into one community race.
public class AlreadyAMemberException() : Exception("That person is already a member of this community.");
