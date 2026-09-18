using AegisScribe.Domain.Managers.Models.Domain;

namespace AegisScribe.Domain.Managers.Models.ServiceModels;

// What a LIVE token is worth telling its holder before they decide to sign in.
//
// This shape exists only for a live invitation. Every refusal — expired, consumed, revoked, unknown —
// carries none of it, because naming the community to somebody holding a dead token tells them
// something the token no longer entitles them to.
public class InvitationPreviewServiceModel
{
    public string TenantSlug { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;

    // What accepting would make them. Shown up front so nobody accepts blind.
    public TenantRole Role { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
}

// The result of spending one. Carries the slug because the SPA's next move is to land the new member
// in /t/:slug, and making it re-read /me to find out where they just joined would be a round-trip for
// something this response already knows.
public class InvitationAcceptedServiceModel
{
    public string TenantSlug { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;

    // Their role in the community NOW — which for somebody who was already a member is the role they
    // already had, not the one the token offered. Accepting never changes a standing role in either
    // direction.
    public TenantRole Role { get; set; }

    /// <summary>True when they were already in, so nothing was created and the token was not spent.</summary>
    public bool AlreadyAMember { get; set; }
}
