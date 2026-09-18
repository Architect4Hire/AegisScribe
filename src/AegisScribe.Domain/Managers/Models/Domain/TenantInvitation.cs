namespace AegisScribe.Domain.Managers.Models.Domain;

// An officer's single-use, expiring offer of membership at one role in one community.
//
// Deliberately NOT ITenantScoped, for the same reason as TenantMembership and by the same bargain.
// The invitee accepts BEFORE they are a member, on a tenant-less route, and the token is the only
// thing that names the community — so there is no resolved tenant for a query filter to read or for
// the stamping interceptor to stamp from. Every query here except the accept-time lookup therefore
// passes TenantId explicitly (see ITenantInvitationRepository); the accept path is keyed by the
// globally-unique token hash and derives the tenant FROM the row it finds. Do not "fix" this by
// adding ITenantScoped — it would throw on the one lookup that matters.
public class TenantInvitation
{
    public Guid Id { get; set; }

    // Carried, filtered on explicitly, never stamped.
    public Guid TenantId { get; set; }

    /// <summary>SHA-256 of the token. The token itself is never stored.</summary>
    /// <remarks>
    /// A live token grants membership in a community to whoever holds it, which makes it a bearer
    /// secret with the same handling rules as a password: a leaked backup, a stray log line or an
    /// officer-readable invitation list must not yield a usable one. The plaintext exists exactly
    /// once, in the response that created it.
    /// </remarks>
    public byte[] TokenHash { get; set; } = [];

    // What the token grants. An officer cannot mint one above their own rank — a Business rule,
    // because it compares the actor's role to this value.
    public TenantRole Role { get; set; }

    // Free text so an officer can tell two outstanding invitations apart, since we do not send them:
    // they copy a link and pass it on themselves. Externally-sourced text about a person, so anything
    // that later puts it near a prompt treats it as untrusted (ai.md).
    public string? Note { get; set; }

    public string CreatedByUserId { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    // The three terminal states, as two nullable pairs plus the clock. Expiry is DERIVED from
    // ExpiresAt rather than stored: a status column saying "Pending" an hour after the row expired
    // would need a sweeper to stay true, and would be wrong in the meantime.
    public DateTimeOffset? AcceptedAt { get; set; }

    public string? AcceptedByUserId { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public string? RevokedByUserId { get; set; }
}
