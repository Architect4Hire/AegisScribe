namespace AegisScribe.Domain.Data;

// What SignInManager.CheckPasswordSignInAsync's SignInResult collapses down to for this app's one
// caller. Invalid covers both "wrong password" and (from the business layer) "no such user" — the
// sign-in form shows one message for those two. LockedOut is its own outcome: unlike an unknown email
// or a wrong password, it can only ever be reported for an account that exists, so surfacing it is a
// deliberate, narrow exception to the "no oracle" rule above, accepted for the UX benefit of telling a
// locked-out member why they can't get in rather than leaving them guessing at their own password.
public enum CredentialCheckResult
{
    Invalid,
    Success,
    LockedOut,
}
