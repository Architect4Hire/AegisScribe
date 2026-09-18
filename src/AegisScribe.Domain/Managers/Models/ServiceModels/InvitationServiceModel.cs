using AegisScribe.Domain.Managers.Models.Domain;

namespace AegisScribe.Domain.Managers.Models.ServiceModels;

// An outstanding or spent invitation, as the officer's list shows it.
//
// There is no token on this model, and that absence is the design: the plaintext exists once, in
// CreatedInvitationServiceModel, and is never readable again. A list endpoint that handed tokens back
// would turn officer-level read access into the ability to join as anybody.
public class InvitationServiceModel
{
    public Guid Id { get; set; }
    public TenantRole Role { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }

    // Derived from the clock and the two nullable pairs, never stored — see the entity.
    public InvitationStatus Status { get; set; }
}

public enum InvitationStatus
{
    Pending,
    Accepted,
    Revoked,
    Expired,
}

// The create response, and the only time the token is ever on the wire outbound.
public class CreatedInvitationServiceModel
{
    public required InvitationServiceModel Invitation { get; set; }

    /// <summary>The plaintext token. Shown once; the server keeps only its hash.</summary>
    public required string Token { get; set; }
}
