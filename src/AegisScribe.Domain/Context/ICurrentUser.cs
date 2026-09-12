namespace AegisScribe.Domain.Context;

// The caller, as the resource server authenticated them. Implemented by the host (the API reads it
// from the validated token) so nothing in Domain ever touches HttpContext. Business reads this for
// "is this yours" resource rules; it is never an input a client can supply.
public interface ICurrentUser
{
    // The Identity user id, read the same way UserManager.GetUserId reads it. Null when anonymous,
    // and for a client-credentials token, which names no Identity user.
    string? UserId { get; }

    // The raw sub claim — the user id for a human sign-in, the client id for a machine token.
    string? Subject { get; }

    bool IsPlatformAdmin { get; }
}
