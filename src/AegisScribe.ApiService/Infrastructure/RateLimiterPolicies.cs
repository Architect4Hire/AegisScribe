namespace AegisScribe.ApiService.Infrastructure;

public static class RateLimiterPolicies
{
    // Named policies stack ON TOP of the global limiter in Program.cs — a request passes both. This
    // one exists because the create form asks on every keystroke: debounced that is a handful
    // of calls per community created, but scripted it is a cheap way to walk the slug space, and
    // leaving it in the shared per-sub bucket would let that consume a caller's whole budget.
    public const string SlugCheck = "slug-check";

    // The invitation preview and accept. Both are keyed by a secret rather than by a session, and the
    // preview is anonymous, so the per-sub bucket does not apply to the caller who matters. A 256-bit
    // token makes guessing hopeless on its own; this makes trying cheap to stop as well, and keeps a
    // script hammering /invitations from consuming anything else's budget.
    public const string InvitationToken = "invitation-token";
}
