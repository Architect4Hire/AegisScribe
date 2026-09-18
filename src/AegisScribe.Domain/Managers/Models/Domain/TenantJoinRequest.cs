namespace AegisScribe.Domain.Managers.Models.Domain;

// The other door: a user asks to join, an officer decides.
//
// Deliberately NOT ITenantScoped, same bargain as TenantInvitation and TenantMembership. The row is
// CREATED by somebody who is not a member yet, on a tenant-less route, so tenant resolution has not
// happened and there is nothing for the interceptor to stamp or a filter to read. Every query passes
// TenantId explicitly — see ITenantJoinRequestRepository, where it is a parameter on every method.
public class TenantJoinRequest
{
    public Guid Id { get; set; }

    // Carried, filtered on explicitly, never stamped.
    public Guid TenantId { get; set; }

    public string UserId { get; set; } = string.Empty;

    // The applicant's own words. Written by somebody outside the community — untrusted input the
    // moment it goes near a prompt (ai.md).
    public string? Message { get; set; }

    public DateTimeOffset RequestedAt { get; set; }

    public JoinRequestStatus Status { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    public string? DecidedByUserId { get; set; }
}

// Persisted as its NAME, not its number — these rows outlive the code that wrote them, and an integer
// enum would make inserting a member a silent rewriting of history. Same rule as AuditAction.
public enum JoinRequestStatus
{
    Pending,
    Approved,
    Declined,
}
