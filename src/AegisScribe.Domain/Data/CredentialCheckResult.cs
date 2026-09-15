namespace AegisScribe.Domain.Data;

// What SignInManager's SignInResult collapses to for this app. Invalid covers both "wrong password"
// and "no such user", which the sign-in form shows one message for.
//
// LockedOut is its own outcome, and a deliberate narrow exception to that: it can only be reported for
// an account that exists, accepted so a locked-out member is told why rather than left guessing at
// their own password.
public enum CredentialCheckResult
{
    Invalid,
    Success,
    LockedOut,
}
